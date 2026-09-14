package com.smartwheelchair.harness.activation;

import com.fasterxml.jackson.annotation.JsonProperty;
import com.smartwheelchair.harness.api.ActivationDtos;
import com.smartwheelchair.harness.api.ApiException;
import com.smartwheelchair.harness.config.GatewayProperties;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Component;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;

import java.math.RoundingMode;
import java.util.List;
import java.util.Map;

@Component
public final class LiteLlmKeyIssuer {
    private final RestClient client;
    private final GatewayProperties properties;

    @Autowired
    public LiteLlmKeyIssuer(GatewayProperties properties) {
        this(RestClient.builder(), properties);
    }

    LiteLlmKeyIssuer(RestClient.Builder builder, GatewayProperties properties) {
        this.client = builder.baseUrl(properties.adminBaseUrl().toString()).build();
        this.properties = properties;
    }

    public IssuedKey issue(ActivationDtos.EmployeeProfile employee, ActivationDtos.QuotaSummary quota, List<String> models) {
        var request = new GenerateKeyRequest(
                employee.employeeId().toString().replace("-", ""),
                "employee-" + employee.employeeNumber(),
                List.copyOf(models),
                quota.monthlyBudgetCny().divide(properties.cnyPerUsd(), 4, RoundingMode.DOWN),
                "1mo",
                quota.requestsPerMinute(),
                quota.tokensPerMinute(),
                quota.maxConcurrentRequests(),
                Map.of(
                        "employee_number", employee.employeeNumber(),
                        "department", employee.department(),
                        "budget_currency", "CNY",
                        "monthly_budget_cny", quota.monthlyBudgetCny().toPlainString(),
                        "cny_per_usd", properties.cnyPerUsd().toPlainString()));
        try {
            var response = client.post()
                    .uri("/key/generate")
                    .header(HttpHeaders.AUTHORIZATION, "Bearer " + properties.masterKey())
                    .body(request)
                    .retrieve()
                    .body(GenerateKeyResponse.class);
            if (response == null || response.key() == null || response.key().isBlank()
                    || response.tokenId() == null || response.tokenId().isBlank()) {
                throw unavailable();
            }
            return new IssuedKey(response.key(), response.tokenId());
        } catch (RestClientException exception) {
            throw unavailable();
        }
    }

    public void revoke(List<String> tokenIds) {
        if (tokenIds.isEmpty()) {
            return;
        }
        try {
            client.post()
                    .uri("/key/delete")
                    .header(HttpHeaders.AUTHORIZATION, "Bearer " + properties.masterKey())
                    .body(new DeleteKeyRequest(List.copyOf(tokenIds)))
                    .retrieve()
                    .toBodilessEntity();
        } catch (RestClientException exception) {
            throw unavailable();
        }
    }

    public void updateModels(List<String> tokenIds, List<String> models) {
        try {
            for (var tokenId : tokenIds) {
                client.post()
                        .uri("/key/update")
                        .header(HttpHeaders.AUTHORIZATION, "Bearer " + properties.masterKey())
                        .body(new UpdateKeyRequest(tokenId, List.copyOf(models)))
                        .retrieve()
                        .toBodilessEntity();
            }
        } catch (RestClientException exception) {
            throw unavailable();
        }
    }

    private static ApiException unavailable() {
        return new ApiException("gateway.unavailable", "模型网关暂时不可用，请稍后重试。", HttpStatus.SERVICE_UNAVAILABLE);
    }

    public record IssuedKey(String secret, String tokenId) {
    }

    private record GenerateKeyRequest(
            @JsonProperty("user_id") String userId,
            @JsonProperty("key_alias") String keyAlias,
            List<String> models,
            @JsonProperty("max_budget") java.math.BigDecimal maxBudget,
            @JsonProperty("budget_duration") String budgetDuration,
            @JsonProperty("rpm_limit") int rpmLimit,
            @JsonProperty("tpm_limit") int tpmLimit,
            @JsonProperty("max_parallel_requests") int maxParallelRequests,
            Map<String, String> metadata) {
    }

    private record GenerateKeyResponse(
            String key,
            @JsonProperty("token_id") String tokenId) {
    }

    private record DeleteKeyRequest(List<String> keys) {
    }

    private record UpdateKeyRequest(String key, List<String> models) {
    }
}
