package com.smartwheelchair.harness.activation;

import com.smartwheelchair.harness.api.ActivationDtos;
import com.smartwheelchair.harness.api.ApiException;
import com.smartwheelchair.harness.config.ActivationProperties;
import com.smartwheelchair.harness.config.GatewayProperties;
import com.smartwheelchair.harness.models.ModelCatalogService;
import org.springframework.http.HttpStatus;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.math.BigDecimal;
import java.security.SecureRandom;
import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.temporal.ChronoUnit;
import java.time.temporal.TemporalAdjusters;
import java.util.HexFormat;
import java.util.List;
import java.util.UUID;

@Service
public class ActivationService {
    private static final SecureRandom RANDOM = new SecureRandom();
    private final NamedParameterJdbcTemplate jdbc;
    private final SecretHasher hasher;
    private final LiteLlmKeyIssuer keyIssuer;
    private final ActivationProperties activationProperties;
    private final GatewayProperties gatewayProperties;
    private final ModelCatalogService modelCatalogService;
    private final Clock clock;

    @Autowired
    public ActivationService(
            NamedParameterJdbcTemplate jdbc,
            SecretHasher hasher,
            LiteLlmKeyIssuer keyIssuer,
            ActivationProperties activationProperties,
            GatewayProperties gatewayProperties,
            ModelCatalogService modelCatalogService) {
        this(jdbc, hasher, keyIssuer, activationProperties, gatewayProperties, modelCatalogService, Clock.systemUTC());
    }

    ActivationService(
            NamedParameterJdbcTemplate jdbc,
            SecretHasher hasher,
            LiteLlmKeyIssuer keyIssuer,
            ActivationProperties activationProperties,
            GatewayProperties gatewayProperties,
            ModelCatalogService modelCatalogService,
            Clock clock) {
        this.jdbc = jdbc;
        this.hasher = hasher;
        this.keyIssuer = keyIssuer;
        this.activationProperties = activationProperties;
        this.gatewayProperties = gatewayProperties;
        this.modelCatalogService = modelCatalogService;
        this.clock = clock;
    }

    @Transactional
    public ActivationDtos.PreviewResponse preview(ActivationDtos.PreviewRequest request) {
        var rows = jdbc.query("""
                        select c.id as activation_code_id, c.expires_at, c.redeemed_at, c.revoked_at,
                               e.id as employee_id, e.employee_number, e.display_name, e.status,
                               e.access_expires_at,
                               e.monthly_budget_cny, e.rpm_limit, e.tpm_limit, e.max_concurrent_requests,
                               d.name as department
                        from company_harness.activation_codes c
                        join company_harness.employees e on e.id = c.employee_id
                        join company_harness.departments d on d.id = e.department_id
                        where c.code_hash = :codeHash
                        for share of c
                        """,
                new MapSqlParameterSource("codeHash", hasher.activationCode(request.activationCode())),
                (resultSet, rowNumber) -> readCode(resultSet));
        if (rows.isEmpty()) {
            throw error("activation.invalid", "激活码无效。", HttpStatus.BAD_REQUEST);
        }

        var row = rows.getFirst();
        var now = OffsetDateTime.now(clock);
        validate(row, now);
        var expiresAt = min(row.expiresAt(), now.plusMinutes(activationProperties.previewSessionMinutes()));
        var sessionId = randomHex(32);
        jdbc.update("""
                        insert into company_harness.activation_sessions
                            (activation_code_id, session_hash, expires_at)
                        values (:activationCodeId, :sessionHash, :expiresAt)
                        """,
                new MapSqlParameterSource()
                        .addValue("activationCodeId", row.activationCodeId())
                        .addValue("sessionHash", hasher.session(sessionId))
                        .addValue("expiresAt", expiresAt));
        return new ActivationDtos.PreviewResponse(sessionId, row.employee(), expiresAt);
    }

    @Transactional
    public ActivationDtos.ConfirmResponse confirm(ActivationDtos.ConfirmRequest request) {
        var rows = jdbc.query("""
                        select s.id as session_id, s.expires_at as session_expires_at, s.consumed_at,
                               c.id as activation_code_id, c.expires_at, c.redeemed_at, c.revoked_at,
                               e.id as employee_id, e.employee_number, e.display_name, e.status,
                               e.access_expires_at,
                               e.monthly_budget_cny, e.rpm_limit, e.tpm_limit, e.max_concurrent_requests,
                               d.name as department
                        from company_harness.activation_sessions s
                        join company_harness.activation_codes c on c.id = s.activation_code_id
                        join company_harness.employees e on e.id = c.employee_id
                        join company_harness.departments d on d.id = e.department_id
                        where s.session_hash = :sessionHash
                        for update of s, c
                        """,
                new MapSqlParameterSource("sessionHash", hasher.session(request.activationSessionId())),
                (resultSet, rowNumber) -> new RedemptionRow(
                        resultSet.getObject("session_id", UUID.class),
                        resultSet.getObject("session_expires_at", OffsetDateTime.class),
                        resultSet.getObject("consumed_at", OffsetDateTime.class),
                        readCode(resultSet)));
        if (rows.isEmpty()) {
            throw error("activation.session_invalid", "激活确认已失效，请重新输入激活码。", HttpStatus.BAD_REQUEST);
        }

        var redemption = rows.getFirst();
        var now = OffsetDateTime.now(clock);
        if (redemption.consumedAt() != null) {
            throw error("activation.session_invalid", "激活确认已失效，请重新输入激活码。", HttpStatus.BAD_REQUEST);
        }
        if (!redemption.sessionExpiresAt().isAfter(now)) {
            throw error("activation.session_expired", "激活确认已超时，请重新输入激活码。", HttpStatus.GONE);
        }

        validate(redemption.code(), now);
        var quota = quota(redemption.code(), now);
        var issuedKey = keyIssuer.issue(redemption.code().employee(), quota, modelCatalogService.keyModelIds());
        jdbc.update("""
                        insert into company_harness.issued_virtual_keys
                            (employee_id, litellm_token_id, key_alias, status)
                        values (:employeeId, :tokenId, :keyAlias, 'active')
                        """,
                new MapSqlParameterSource()
                        .addValue("employeeId", redemption.code().employee().employeeId())
                        .addValue("tokenId", issuedKey.tokenId())
                        .addValue("keyAlias", "employee-" + redemption.code().employee().employeeNumber()));
        jdbc.update("""
                        update company_harness.activation_codes
                        set redeemed_at = :now
                        where id = :activationCodeId and redeemed_at is null
                        """,
                new MapSqlParameterSource()
                        .addValue("now", now)
                        .addValue("activationCodeId", redemption.code().activationCodeId()));
        jdbc.update("""
                        update company_harness.activation_sessions
                        set consumed_at = :now
                        where id = :sessionId and consumed_at is null
                        """,
                new MapSqlParameterSource()
                        .addValue("now", now)
                        .addValue("sessionId", redemption.sessionId()));
        return new ActivationDtos.ConfirmResponse(
                redemption.code().employee(),
                issuedKey.secret(),
                gatewayProperties.publicBaseUrl(),
                quota,
                new ActivationDtos.ClientPolicy(
                        modelCatalogService.enabledModelIds(),
                        modelCatalogService.publicCatalog().defaultModel(),
                        modelCatalogService.publicCatalog().models(),
                        false,
                        false));
    }

    private static CodeRow readCode(java.sql.ResultSet resultSet) throws java.sql.SQLException {
        return new CodeRow(
                resultSet.getObject("activation_code_id", UUID.class),
                resultSet.getObject("expires_at", OffsetDateTime.class),
                resultSet.getObject("redeemed_at", OffsetDateTime.class),
                resultSet.getObject("revoked_at", OffsetDateTime.class),
                new ActivationDtos.EmployeeProfile(
                        resultSet.getObject("employee_id", UUID.class),
                        resultSet.getString("employee_number"),
                        resultSet.getString("display_name"),
                        resultSet.getString("department")),
                resultSet.getString("status"),
                resultSet.getObject("access_expires_at", OffsetDateTime.class),
                resultSet.getBigDecimal("monthly_budget_cny"),
                resultSet.getInt("rpm_limit"),
                resultSet.getInt("tpm_limit"),
                resultSet.getInt("max_concurrent_requests"));
    }

    private static void validate(CodeRow row, OffsetDateTime now) {
        if (!"active".equals(row.employeeStatus())) {
            throw error("activation.employee_disabled", "员工账号已停用，请联系管理员。", HttpStatus.FORBIDDEN);
        }
        if (row.accessExpiresAt() != null && !row.accessExpiresAt().isAfter(now)) {
            throw error("activation.employee_access_expired", "员工访问期限已到，请联系管理员。", HttpStatus.FORBIDDEN);
        }
        if (row.revokedAt() != null) {
            throw error("activation.revoked", "激活码已撤销，请联系管理员重新签发。", HttpStatus.GONE);
        }
        if (row.redeemedAt() != null) {
            throw error("activation.redeemed", "激活码已使用，请联系管理员重新签发。", HttpStatus.CONFLICT);
        }
        if (!row.expiresAt().isAfter(now)) {
            throw error("activation.expired", "激活码已过期，请联系管理员重新签发。", HttpStatus.GONE);
        }
    }

    private static ActivationDtos.QuotaSummary quota(CodeRow row, OffsetDateTime now) {
        var resetsAt = now.atZoneSameInstant(ZoneId.of("Asia/Shanghai"))
                .with(TemporalAdjusters.firstDayOfNextMonth())
                .truncatedTo(ChronoUnit.DAYS)
                .toOffsetDateTime();
        return new ActivationDtos.QuotaSummary(
                row.monthlyBudgetCny(), BigDecimal.ZERO, row.rpmLimit(), row.tpmLimit(),
                row.maxConcurrentRequests(), resetsAt);
    }

    private static String randomHex(int bytes) {
        var value = new byte[bytes];
        RANDOM.nextBytes(value);
        return HexFormat.of().withUpperCase().formatHex(value);
    }

    private static OffsetDateTime min(OffsetDateTime left, OffsetDateTime right) {
        return left.isBefore(right) ? left : right;
    }

    private static ApiException error(String code, String message, HttpStatus status) {
        return new ApiException(code, message, status);
    }

    private record CodeRow(
            UUID activationCodeId,
            OffsetDateTime expiresAt,
            OffsetDateTime redeemedAt,
            OffsetDateTime revokedAt,
            ActivationDtos.EmployeeProfile employee,
            String employeeStatus,
            OffsetDateTime accessExpiresAt,
            BigDecimal monthlyBudgetCny,
            int rpmLimit,
            int tpmLimit,
            int maxConcurrentRequests) {
    }

    private record RedemptionRow(
            UUID sessionId,
            OffsetDateTime sessionExpiresAt,
            OffsetDateTime consumedAt,
            CodeRow code) {
    }
}
