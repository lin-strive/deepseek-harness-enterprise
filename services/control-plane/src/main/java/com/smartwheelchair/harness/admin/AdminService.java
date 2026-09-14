package com.smartwheelchair.harness.admin;

import com.smartwheelchair.harness.activation.LiteLlmKeyIssuer;
import com.smartwheelchair.harness.activation.SecretHasher;
import com.smartwheelchair.harness.api.AdminDtos;
import com.smartwheelchair.harness.api.ApiException;
import org.apache.commons.csv.CSVFormat;
import org.apache.commons.csv.CSVRecord;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpStatus;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.multipart.MultipartFile;

import java.io.IOException;
import java.io.InputStreamReader;
import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

@Service
public class AdminService {
    private static final SecureRandom RANDOM = new SecureRandom();
    private static final BigDecimal MAX_MONTHLY_BUDGET = new BigDecimal(AdminDtos.MAX_MONTHLY_BUDGET_CNY);
    private static final List<String> REQUIRED_HEADERS = List.of(
            "employee_number", "display_name", "department", "monthly_budget_cny",
            "rpm_limit", "tpm_limit", "max_concurrent_requests");
    private final NamedParameterJdbcTemplate jdbc;
    private final SecretHasher hasher;
    private final LiteLlmKeyIssuer keyIssuer;
    private final Clock clock;

    @Autowired
    public AdminService(NamedParameterJdbcTemplate jdbc, SecretHasher hasher, LiteLlmKeyIssuer keyIssuer) {
        this(jdbc, hasher, keyIssuer, Clock.systemUTC());
    }

    AdminService(NamedParameterJdbcTemplate jdbc, SecretHasher hasher, LiteLlmKeyIssuer keyIssuer, Clock clock) {
        this.jdbc = jdbc;
        this.hasher = hasher;
        this.keyIssuer = keyIssuer;
        this.clock = clock;
    }

    public AdminDtos.OverviewResponse overview() {
        return new AdminDtos.OverviewResponse(
                count("select count(*) from company_harness.employees"),
                count("""
                        select count(*) from company_harness.employees
                        where status = 'active'
                          and (access_expires_at is null or access_expires_at > now())
                        """),
                count("select count(*) from company_harness.departments"),
                count("""
                        select count(*) from company_harness.activation_codes
                        where redeemed_at is null and revoked_at is null and expires_at > now()
                        """),
                count("select count(*) from company_harness.issued_virtual_keys where status = 'active'"));
    }

    public List<AdminDtos.EmployeeRow> employees() {
        return jdbc.query("""
                        select e.id, e.employee_number, e.display_name, d.name as department, e.status,
                               e.access_expires_at,
                               e.monthly_budget_cny, e.rpm_limit, e.tpm_limit,
                               e.max_concurrent_requests, e.updated_at
                        from company_harness.employees e
                        join company_harness.departments d on d.id = e.department_id
                        order by e.employee_number
                        """,
                Map.of(),
                (resultSet, rowNumber) -> readEmployee(resultSet));
    }

    @Transactional
    public AdminDtos.EmployeeRow createEmployee(AdminDtos.CreateEmployeeRequest request, String actor) {
        var employeeNumber = normalizeEmployeeNumber(request.employeeNumber());
        var departmentId = departmentId(request.department().trim());
        var accessExpiresAt = ValidityPolicy.accessExpiry(request.accessExpiresAt(), OffsetDateTime.now(clock));
        UUID employeeId;
        try {
            employeeId = jdbc.queryForObject("""
                            insert into company_harness.employees (
                                employee_number, display_name, department_id, status,
                                access_expires_at, monthly_budget_cny, rpm_limit, tpm_limit, max_concurrent_requests)
                            values (
                                :employeeNumber, :displayName, :departmentId, :status,
                                :accessExpiresAt, :monthlyBudgetCny, :rpmLimit, :tpmLimit, :maxConcurrentRequests)
                            returning id
                            """,
                    new MapSqlParameterSource()
                            .addValue("employeeNumber", employeeNumber)
                            .addValue("displayName", request.displayName().trim())
                            .addValue("departmentId", departmentId)
                            .addValue("status", request.status())
                            .addValue("accessExpiresAt", accessExpiresAt)
                            .addValue("monthlyBudgetCny", request.monthlyBudgetCny())
                            .addValue("rpmLimit", request.requestsPerMinute())
                            .addValue("tpmLimit", request.tokensPerMinute())
                            .addValue("maxConcurrentRequests", request.maxConcurrentRequests()),
                    UUID.class);
        } catch (DuplicateKeyException exception) {
            throw new ApiException("employee.already_exists", "员工编号 " + employeeNumber + " 已存在。", HttpStatus.CONFLICT);
        }
        audit(actor, "employees.create", "employee", employeeNumber, "{}");
        return employee(employeeId);
    }

    @Transactional
    public AdminDtos.EmployeeMutationResponse changeEmployeeStatus(
            String employeeNumber,
            AdminDtos.EmployeeStatusRequest request,
            String actor) {
        var employee = employeeState(employeeNumber);
        if (employee.status().equals(request.status())) {
            return new AdminDtos.EmployeeMutationResponse(employee(employee.id()), 0, 0);
        }

        var now = OffsetDateTime.now(clock);
        if ("active".equals(request.status())
                && employee.accessExpiresAt() != null
                && !employee.accessExpiresAt().isAfter(now)) {
            throw new ApiException(
                    "employee.access_expired",
                    "员工访问期限已到，请先设置新的结束时间或改为长期有效。",
                    HttpStatus.CONFLICT);
        }

        var revocation = RevocationResult.NONE;
        if ("disabled".equals(request.status())) {
            revocation = revokeEmployeeAccess(employee.id(), now);
        }

        jdbc.update("""
                        update company_harness.employees
                        set status = :status, row_version = row_version + 1, updated_at = now()
                        where id = :employeeId
                        """,
                new MapSqlParameterSource()
                        .addValue("employeeId", employee.id())
                        .addValue("status", request.status()));
        audit(actor, "employees.status_change", "employee", employee.employeeNumber(),
                "{\"status\":\"" + request.status() + "\",\"revoked_activation_codes\":" + revocation.codes()
                        + ",\"revoked_virtual_keys\":" + revocation.keys() + "}");
        return new AdminDtos.EmployeeMutationResponse(employee(employee.id()), revocation.codes(), revocation.keys());
    }

    @Transactional
    public AdminDtos.EmployeeRow changeEmployeeAccessExpiry(
            String employeeNumber,
            AdminDtos.EmployeeAccessExpiryRequest request,
            String actor) {
        var employee = employeeState(employeeNumber);
        var accessExpiresAt = ValidityPolicy.accessExpiry(request.accessExpiresAt(), OffsetDateTime.now(clock));
        jdbc.update("""
                        update company_harness.employees
                        set access_expires_at = :accessExpiresAt,
                            row_version = row_version + 1,
                            updated_at = now()
                        where id = :employeeId
                        """,
                new MapSqlParameterSource()
                        .addValue("employeeId", employee.id())
                        .addValue("accessExpiresAt", accessExpiresAt));
        var expiryMetadata = accessExpiresAt == null ? "null" : "\"" + accessExpiresAt + "\"";
        audit(actor, "employees.access_expiry_change", "employee", employee.employeeNumber(),
                "{\"access_expires_at\":" + expiryMetadata + "}");
        return employee(employee.id());
    }

    @Transactional
    public boolean expireEmployeeAccess(String employeeNumber) {
        var employee = employeeState(employeeNumber);
        var now = OffsetDateTime.now(clock);
        if (!"active".equals(employee.status())
                || employee.accessExpiresAt() == null
                || employee.accessExpiresAt().isAfter(now)) {
            return false;
        }

        var revocation = revokeEmployeeAccess(employee.id(), now);
        jdbc.update("""
                        update company_harness.employees
                        set status = 'disabled', row_version = row_version + 1, updated_at = :now
                        where id = :employeeId
                        """,
                new MapSqlParameterSource()
                        .addValue("employeeId", employee.id())
                        .addValue("now", now));
        audit("system:access-expiry", "employees.access_expired", "employee", employee.employeeNumber(),
                "{\"revoked_activation_codes\":" + revocation.codes()
                        + ",\"revoked_virtual_keys\":" + revocation.keys() + "}");
        return true;
    }

    @Transactional
    public void deleteEmployee(String employeeNumber, String actor) {
        var employee = employeeState(employeeNumber);
        if (count("""
                        select count(*) from company_harness.issued_virtual_keys
                        where employee_id = :employeeId
                        """, new MapSqlParameterSource("employeeId", employee.id())) > 0) {
            throw new ApiException(
                    "employee.has_key_history",
                    "该员工已签发过模型密钥，不能删除；请使用停用。",
                    HttpStatus.CONFLICT);
        }

        var parameters = new MapSqlParameterSource("employeeId", employee.id());
        jdbc.update("""
                        delete from company_harness.activation_sessions
                        where activation_code_id in (
                            select id from company_harness.activation_codes where employee_id = :employeeId)
                        """, parameters);
        jdbc.update("delete from company_harness.activation_codes where employee_id = :employeeId", parameters);
        jdbc.update("delete from company_harness.employees where id = :employeeId", parameters);
        audit(actor, "employees.delete", "employee", employee.employeeNumber(), "{}");
        jdbc.update("""
                        delete from company_harness.departments d
                        where d.id = :departmentId
                          and not exists (select 1 from company_harness.employees e where e.department_id = d.id)
                        """,
                new MapSqlParameterSource("departmentId", employee.departmentId()));
    }

    @Transactional
    public AdminDtos.ImportResponse importEmployees(MultipartFile file, String actor) {
        if (file.isEmpty()) {
            throw badRequest("csv.empty", "请选择包含员工资料的 CSV 文件。");
        }

        var imported = new ArrayList<String>();
        try (var reader = new InputStreamReader(file.getInputStream(), StandardCharsets.UTF_8);
             var parser = CSVFormat.DEFAULT.builder()
                     .setHeader()
                     .setSkipHeaderRecord(true)
                     .setIgnoreEmptyLines(true)
                     .setTrim(true)
                     .get()
                     .parse(reader)) {
            var headers = parser.getHeaderMap().keySet();
            if (!headers.containsAll(REQUIRED_HEADERS)) {
                throw badRequest("csv.headers_invalid", "CSV 缺少必需列，请下载模板后重试。");
            }

            for (var record : parser) {
                var employee = parse(record);
                upsert(employee);
                imported.add(employee.employeeNumber());
            }
        } catch (IOException exception) {
            throw badRequest("csv.read_failed", "CSV 文件无法读取，请确认使用 UTF-8 编码。");
        }

        if (imported.isEmpty()) {
            throw badRequest("csv.no_rows", "CSV 中没有可导入的员工记录。");
        }
        audit(actor, "employees.import", "employee", "batch", "{\"count\":" + imported.size() + "}");
        return new AdminDtos.ImportResponse(imported.size(), List.copyOf(imported));
    }

    @Transactional
    public AdminDtos.BatchActivationResponse generateCodes(
            AdminDtos.BatchActivationRequest request,
            String actor) {
        var employeeNumbers = new LinkedHashSet<String>();
        for (var value : request.employeeNumbers()) {
            if (value != null && !value.isBlank()) {
                employeeNumbers.add(value.trim().toUpperCase(Locale.ROOT));
            }
        }
        if (employeeNumbers.isEmpty() || employeeNumbers.size() > 500) {
            throw badRequest("activation.batch_size_invalid", "每次请选择 1 至 500 名员工。");
        }

        var now = OffsetDateTime.now(clock);
        var requestedExpiresAt = ValidityPolicy.activationCodeExpiry(
                request.expiresAt(), request.expiresInHours(), now);
        var generated = new ArrayList<AdminDtos.GeneratedActivationCode>();
        for (var employeeNumber : employeeNumbers) {
            var employees = jdbc.query("""
                            select id, employee_number, display_name, status, access_expires_at
                            from company_harness.employees
                            where employee_number = :employeeNumber
                            for update
                            """,
                    new MapSqlParameterSource("employeeNumber", employeeNumber),
                    (resultSet, rowNumber) -> new EmployeeIdentity(
                            resultSet.getObject("id", UUID.class),
                            resultSet.getString("employee_number"),
                            resultSet.getString("display_name"),
                            resultSet.getString("status"),
                            resultSet.getObject("access_expires_at", OffsetDateTime.class)));
            if (employees.isEmpty()) {
                throw badRequest("employee.not_found", "员工 " + employeeNumber + " 不存在。");
            }
            var employee = employees.getFirst();
            if (!"active".equals(employee.status())) {
                throw new ApiException("employee.disabled", "员工 " + employeeNumber + " 已停用。", HttpStatus.CONFLICT);
            }
            if (employee.accessExpiresAt() != null && !employee.accessExpiresAt().isAfter(now)) {
                throw new ApiException("employee.access_expired", "员工 " + employeeNumber + " 的访问期限已到。", HttpStatus.CONFLICT);
            }
            var expiresAt = employee.accessExpiresAt() == null
                    ? requestedExpiresAt
                    : min(requestedExpiresAt, employee.accessExpiresAt());

            if (request.replaceExisting()) {
                jdbc.update("""
                                update company_harness.activation_codes
                                set revoked_at = :now
                                where employee_id = :employeeId
                                  and redeemed_at is null and revoked_at is null and expires_at > :now
                                """,
                        new MapSqlParameterSource()
                                .addValue("now", now)
                                .addValue("employeeId", employee.id()));
            }
            var plaintext = "SW-" + randomHex(12);
            jdbc.update("""
                            insert into company_harness.activation_codes
                                (employee_id, code_hash, expires_at, created_by)
                            values (:employeeId, :codeHash, :expiresAt, :createdBy)
                            """,
                    new MapSqlParameterSource()
                            .addValue("employeeId", employee.id())
                            .addValue("codeHash", hasher.activationCode(plaintext))
                            .addValue("expiresAt", expiresAt)
                            .addValue("createdBy", actorId(actor)));
            generated.add(new AdminDtos.GeneratedActivationCode(
                    employee.employeeNumber(), employee.displayName(), plaintext, expiresAt));
        }

        audit(actor, "activation_codes.generate", "activation_code", "batch",
                "{\"count\":" + generated.size()
                        + ",\"requested_expires_at\":\"" + requestedExpiresAt + "\"}");
        return new AdminDtos.BatchActivationResponse(List.copyOf(generated));
    }

    private void upsert(EmployeeImport employee) {
        var departmentId = departmentId(employee.department());
        jdbc.queryForObject("""
                        insert into company_harness.employees (
                            employee_number, display_name, department_id, status,
                            access_expires_at, monthly_budget_cny, rpm_limit, tpm_limit, max_concurrent_requests)
                        values (
                            :employeeNumber, :displayName, :departmentId, :status,
                            :accessExpiresAt, :monthlyBudgetCny, :rpmLimit, :tpmLimit, :maxConcurrentRequests)
                        on conflict (employee_number) do update set
                            display_name = excluded.display_name,
                            department_id = excluded.department_id,
                            status = excluded.status,
                            access_expires_at = case
                                when :accessExpiryProvided then excluded.access_expires_at
                                else company_harness.employees.access_expires_at
                            end,
                            monthly_budget_cny = excluded.monthly_budget_cny,
                            rpm_limit = excluded.rpm_limit,
                            tpm_limit = excluded.tpm_limit,
                            max_concurrent_requests = excluded.max_concurrent_requests,
                            row_version = company_harness.employees.row_version + 1,
                            updated_at = now()
                        returning id
                        """,
                new MapSqlParameterSource()
                        .addValue("employeeNumber", employee.employeeNumber())
                        .addValue("displayName", employee.displayName())
                        .addValue("departmentId", departmentId)
                        .addValue("status", employee.status())
                        .addValue("accessExpiresAt", employee.accessExpiresAt())
                        .addValue("accessExpiryProvided", employee.accessExpiryProvided())
                        .addValue("monthlyBudgetCny", employee.monthlyBudgetCny())
                        .addValue("rpmLimit", employee.rpmLimit())
                        .addValue("tpmLimit", employee.tpmLimit())
                        .addValue("maxConcurrentRequests", employee.maxConcurrentRequests()),
                UUID.class);
    }

    private UUID departmentId(String department) {
        return jdbc.queryForObject("""
                        insert into company_harness.departments (name)
                        values (:name)
                        on conflict (name) do update set name = excluded.name
                        returning id
                        """,
                new MapSqlParameterSource("name", department),
                UUID.class);
    }

    private AdminDtos.EmployeeRow employee(UUID employeeId) {
        var rows = jdbc.query("""
                        select e.id, e.employee_number, e.display_name, d.name as department, e.status,
                               e.access_expires_at,
                               e.monthly_budget_cny, e.rpm_limit, e.tpm_limit,
                               e.max_concurrent_requests, e.updated_at
                        from company_harness.employees e
                        join company_harness.departments d on d.id = e.department_id
                        where e.id = :employeeId
                        """,
                new MapSqlParameterSource("employeeId", employeeId),
                (resultSet, rowNumber) -> readEmployee(resultSet));
        if (rows.isEmpty()) {
            throw new ApiException("employee.not_found", "员工不存在。", HttpStatus.NOT_FOUND);
        }
        return rows.getFirst();
    }

    private EmployeeState employeeState(String employeeNumber) {
        var normalized = normalizeEmployeeNumber(employeeNumber);
        var rows = jdbc.query("""
                        select id, department_id, employee_number, display_name, status, access_expires_at
                        from company_harness.employees
                        where employee_number = :employeeNumber
                        for update
                        """,
                new MapSqlParameterSource("employeeNumber", normalized),
                (resultSet, rowNumber) -> new EmployeeState(
                        resultSet.getObject("id", UUID.class),
                        resultSet.getObject("department_id", UUID.class),
                        resultSet.getString("employee_number"),
                        resultSet.getString("display_name"),
                        resultSet.getString("status"),
                        resultSet.getObject("access_expires_at", OffsetDateTime.class)));
        if (rows.isEmpty()) {
            throw new ApiException("employee.not_found", "员工 " + normalized + " 不存在。", HttpStatus.NOT_FOUND);
        }
        return rows.getFirst();
    }

    private static AdminDtos.EmployeeRow readEmployee(java.sql.ResultSet resultSet) throws java.sql.SQLException {
        return new AdminDtos.EmployeeRow(
                resultSet.getObject("id", UUID.class),
                resultSet.getString("employee_number"),
                resultSet.getString("display_name"),
                resultSet.getString("department"),
                resultSet.getString("status"),
                resultSet.getObject("access_expires_at", OffsetDateTime.class),
                resultSet.getBigDecimal("monthly_budget_cny"),
                resultSet.getInt("rpm_limit"),
                resultSet.getInt("tpm_limit"),
                resultSet.getInt("max_concurrent_requests"),
                resultSet.getObject("updated_at", OffsetDateTime.class));
    }

    private static String normalizeEmployeeNumber(String employeeNumber) {
        if (employeeNumber == null || employeeNumber.isBlank()) {
            throw badRequest("employee.number_invalid", "员工编号不能为空。");
        }
        return employeeNumber.trim().toUpperCase(Locale.ROOT);
    }

    private EmployeeImport parse(CSVRecord record) {
        try {
            var employeeNumber = required(record, "employee_number").toUpperCase(Locale.ROOT);
            var displayName = required(record, "display_name");
            var department = required(record, "department");
            var status = record.isMapped("status") && !record.get("status").isBlank()
                    ? record.get("status").trim().toLowerCase(Locale.ROOT)
                    : "active";
            if (!status.equals("active") && !status.equals("disabled")) {
                throw new IllegalArgumentException("status");
            }
            var monthlyBudget = new BigDecimal(required(record, "monthly_budget_cny"));
            var rpm = Integer.parseInt(required(record, "rpm_limit"));
            var tpm = Integer.parseInt(required(record, "tpm_limit"));
            var concurrency = Integer.parseInt(required(record, "max_concurrent_requests"));
            var accessExpiryProvided = record.isMapped("access_expires_at");
            OffsetDateTime accessExpiresAt = null;
            if (accessExpiryProvided && !record.get("access_expires_at").isBlank()) {
                accessExpiresAt = ValidityPolicy.accessExpiry(
                        OffsetDateTime.parse(record.get("access_expires_at").trim()),
                        OffsetDateTime.now(clock));
            }
            if (monthlyBudget.signum() < 0
                    || monthlyBudget.compareTo(MAX_MONTHLY_BUDGET) > 0
                    || rpm <= 0 || rpm > AdminDtos.MAX_REQUESTS_PER_MINUTE
                    || tpm <= 0 || tpm > AdminDtos.MAX_TOKENS_PER_MINUTE
                    || concurrency <= 0 || concurrency > AdminDtos.MAX_CONCURRENT_REQUESTS) {
                throw new IllegalArgumentException("quota");
            }
            return new EmployeeImport(
                    employeeNumber, displayName, department, status, accessExpiresAt, accessExpiryProvided,
                    monthlyBudget, rpm, tpm, concurrency);
        } catch (RuntimeException exception) {
            throw badRequest("csv.row_invalid", "CSV 第 " + record.getRecordNumber() + " 行字段无效。");
        }
    }

    private static String required(CSVRecord record, String header) {
        var value = record.get(header).trim();
        if (value.isBlank() || value.length() > 128) {
            throw new IllegalArgumentException(header);
        }
        return value;
    }

    private RevocationResult revokeEmployeeAccess(UUID employeeId, OffsetDateTime now) {
        var parameters = new MapSqlParameterSource()
                .addValue("employeeId", employeeId)
                .addValue("now", now);
        var tokenIds = jdbc.queryForList("""
                        select litellm_token_id
                        from company_harness.issued_virtual_keys
                        where employee_id = :employeeId and status = 'active'
                        order by created_at
                        """,
                parameters,
                String.class);
        keyIssuer.revoke(tokenIds);
        var revokedKeys = jdbc.update("""
                        update company_harness.issued_virtual_keys
                        set status = 'disabled', disabled_at = :now
                        where employee_id = :employeeId and status = 'active'
                        """,
                parameters);
        var revokedCodes = jdbc.update("""
                        update company_harness.activation_codes
                        set revoked_at = :now
                        where employee_id = :employeeId
                          and redeemed_at is null and revoked_at is null and expires_at > :now
                        """,
                parameters);
        return new RevocationResult(revokedCodes, revokedKeys);
    }

    private static OffsetDateTime min(OffsetDateTime left, OffsetDateTime right) {
        return left.isBefore(right) ? left : right;
    }

    private long count(String sql) {
        var value = jdbc.getJdbcTemplate().queryForObject(sql, Long.class);
        return value == null ? 0 : value;
    }

    private long count(String sql, MapSqlParameterSource parameters) {
        var value = jdbc.queryForObject(sql, parameters, Long.class);
        return value == null ? 0 : value;
    }

    private void audit(String actor, String action, String resourceType, String resourceId, String metadata) {
        jdbc.update("""
                        insert into company_harness.admin_audit_events
                            (actor_admin_id, action, resource_type, resource_id, metadata)
                        values (:actorId, :action, :resourceType, :resourceId, cast(:metadata as jsonb))
                        """,
                new MapSqlParameterSource()
                        .addValue("actorId", actorId(actor))
                        .addValue("action", action)
                        .addValue("resourceType", resourceType)
                        .addValue("resourceId", resourceId)
                        .addValue("metadata", metadata));
    }

    private static UUID actorId(String actor) {
        return UUID.nameUUIDFromBytes(actor.getBytes(StandardCharsets.UTF_8));
    }

    private static String randomHex(int bytes) {
        var value = new byte[bytes];
        RANDOM.nextBytes(value);
        return HexFormat.of().withUpperCase().formatHex(value);
    }

    private static ApiException badRequest(String code, String message) {
        return new ApiException(code, message, HttpStatus.BAD_REQUEST);
    }

    private record EmployeeImport(
            String employeeNumber,
            String displayName,
            String department,
            String status,
            OffsetDateTime accessExpiresAt,
            boolean accessExpiryProvided,
            BigDecimal monthlyBudgetCny,
            int rpmLimit,
            int tpmLimit,
            int maxConcurrentRequests) {
    }

    private record EmployeeIdentity(
            UUID id,
            String employeeNumber,
            String displayName,
            String status,
            OffsetDateTime accessExpiresAt) {
    }

    private record EmployeeState(
            UUID id,
            UUID departmentId,
            String employeeNumber,
            String displayName,
            String status,
            OffsetDateTime accessExpiresAt) {
    }

    private record RevocationResult(int codes, int keys) {
        private static final RevocationResult NONE = new RevocationResult(0, 0);
    }
}
