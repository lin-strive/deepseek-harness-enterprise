package com.smartwheelchair.harness.config;

import jakarta.validation.constraints.DecimalMin;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.validation.annotation.Validated;

import java.math.BigDecimal;
import java.net.URI;
import java.util.List;

@Validated
@ConfigurationProperties("harness.gateway")
public record GatewayProperties(
        URI adminBaseUrl,
        URI publicBaseUrl,
        @NotBlank String masterKey,
        @DecimalMin("0.0001") BigDecimal cnyPerUsd,
        @NotEmpty List<String> allowedModels) {
}
