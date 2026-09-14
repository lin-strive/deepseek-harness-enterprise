package com.smartwheelchair.harness.config;

import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.validation.annotation.Validated;

@Validated
@ConfigurationProperties("harness.activation")
public record ActivationProperties(
        @NotBlank String pepper,
        @Min(1) int previewSessionMinutes) {
}
