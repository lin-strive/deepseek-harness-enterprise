package com.smartwheelchair.harness.activation;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class SecretHasherTest {
    private static final String PEPPER = "0123456789abcdef0123456789abcdef";

    @Test
    void activationCodeIsNormalizedAndNeverStoresPlaintext() {
        var hasher = new SecretHasher(PEPPER);

        var canonical = hasher.activationCode("SW-ABC123");

        assertThat(hasher.activationCode("  sw-abc123  ")).isEqualTo(canonical);
        assertThat(canonical).hasSize(64).matches("[0-9A-F]{64}").doesNotContain("ABC123");
    }

    @Test
    void sessionHashIsStableAndPepperMustBeLongEnough() {
        var hasher = new SecretHasher(PEPPER);

        assertThat(hasher.session("session-id"))
                .isEqualTo(hasher.session("session-id"))
                .hasSize(64);
        assertThatThrownBy(() -> new SecretHasher("too-short"))
                .isInstanceOf(IllegalStateException.class);
    }
}
