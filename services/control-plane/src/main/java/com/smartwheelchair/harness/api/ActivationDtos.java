package com.smartwheelchair.harness.api;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

import java.math.BigDecimal;
import java.net.URI;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;

public final class ActivationDtos {
    private ActivationDtos() {
    }

    public record PreviewRequest(
            @NotBlank @Size(max = 128) String activationCode,
            @Size(max = 64) String clientVersion) {
    }

    public record ConfirmRequest(@NotBlank @Size(max = 128) String activationSessionId) {
    }

    public record EmployeeProfile(UUID employeeId, String employeeNumber, String displayName, String department) {
    }

    public record PreviewResponse(
            String activationSessionId,
            EmployeeProfile employee,
            OffsetDateTime expiresAt) {
    }

    public record QuotaSummary(
            BigDecimal monthlyBudgetCny,
            BigDecimal usedThisMonthCny,
            int requestsPerMinute,
            int tokensPerMinute,
            int maxConcurrentRequests,
            OffsetDateTime resetsAt) {
    }

    public record ClientPolicy(
            List<String> allowedModels,
            String defaultModel,
            List<ModelCatalogDtos.ModelEntry> models,
            boolean personalProvidersAllowed,
            boolean telemetryEnabled) {
    }

    public record ConfirmResponse(
            EmployeeProfile employee,
            String virtualKey,
            URI gatewayBaseUrl,
            QuotaSummary quota,
            ClientPolicy policy) {
    }

    public record ApiError(String code, String message, String supportId) {
    }
}
