package com.smartwheelchair.harness.api;

import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.DecimalMin;
import jakarta.validation.constraints.DecimalMax;
import jakarta.validation.constraints.Digits;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;

public final class AdminDtos {
    public static final String MAX_MONTHLY_BUDGET_CNY = "72000.00";
    public static final int MAX_REQUESTS_PER_MINUTE = 600;
    public static final int MAX_TOKENS_PER_MINUTE = 2_000_000;
    public static final int MAX_CONCURRENT_REQUESTS = 20;

    private AdminDtos() {
    }

    public record OverviewResponse(
            long employeeCount,
            long activeEmployeeCount,
            long departmentCount,
            long availableActivationCodeCount,
            long activeVirtualKeyCount) {
    }

    public record EmployeeRow(
            UUID employeeId,
            String employeeNumber,
            String displayName,
            String department,
            String status,
            OffsetDateTime accessExpiresAt,
            BigDecimal monthlyBudgetCny,
            int requestsPerMinute,
            int tokensPerMinute,
            int maxConcurrentRequests,
            OffsetDateTime updatedAt) {
    }

    public record ImportResponse(int importedCount, List<String> employeeNumbers) {
    }

    public record CreateEmployeeRequest(
            @NotBlank @Size(max = 64) @Pattern(regexp = "[A-Za-z0-9._-]+") String employeeNumber,
            @NotBlank @Size(max = 128) String displayName,
            @NotBlank @Size(max = 128) String department,
            @NotBlank @Pattern(regexp = "active|disabled") String status,
            OffsetDateTime accessExpiresAt,
            @NotNull
            @DecimalMin(value = "0.00", message = "月度金额不能小于 0。")
            @DecimalMax(value = MAX_MONTHLY_BUDGET_CNY, message = "月度金额不能超过 72000 元。")
            @Digits(integer = 10, fraction = 2, message = "月度金额最多保留两位小数。")
            BigDecimal monthlyBudgetCny,
            @Min(value = 1, message = "RPM 至少为 1。")
            @Max(value = MAX_REQUESTS_PER_MINUTE, message = "RPM 不能超过 600。")
            int requestsPerMinute,
            @Min(value = 1, message = "TPM 至少为 1。")
            @Max(value = MAX_TOKENS_PER_MINUTE, message = "TPM 不能超过 2000000。")
            int tokensPerMinute,
            @Min(value = 1, message = "并发请求数至少为 1。")
            @Max(value = MAX_CONCURRENT_REQUESTS, message = "并发请求数不能超过 20。")
            int maxConcurrentRequests) {
    }

    public record EmployeeStatusRequest(
            @NotBlank @Pattern(regexp = "active|disabled") String status) {
    }

    public record EmployeeAccessExpiryRequest(OffsetDateTime accessExpiresAt) {
    }

    public record EmployeeMutationResponse(
            EmployeeRow employee,
            int revokedActivationCodeCount,
            int revokedVirtualKeyCount) {
    }

    public record BatchActivationRequest(
            @NotEmpty List<String> employeeNumbers,
            OffsetDateTime expiresAt,
            @Min(1) @Max(2160) Integer expiresInHours,
            boolean replaceExisting) {
    }

    public record GeneratedActivationCode(
            String employeeNumber,
            String displayName,
            String activationCode,
            OffsetDateTime expiresAt) {
    }

    public record BatchActivationResponse(List<GeneratedActivationCode> codes) {
    }
}
