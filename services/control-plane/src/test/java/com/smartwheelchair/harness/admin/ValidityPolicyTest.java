package com.smartwheelchair.harness.admin;

import com.smartwheelchair.harness.api.ApiException;
import org.junit.jupiter.api.Test;

import java.time.OffsetDateTime;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class ValidityPolicyTest {
    private static final OffsetDateTime NOW = OffsetDateTime.parse("2026-09-05T10:00:00+08:00");

    @Test
    void acceptsAnExactActivationExpiryWithinNinetyDays() {
        var requested = NOW.plusDays(90);

        assertThat(ValidityPolicy.activationCodeExpiry(requested, null, NOW)).isEqualTo(requested);
    }

    @Test
    void keepsLegacyHourRequestsCompatible() {
        assertThat(ValidityPolicy.activationCodeExpiry(null, 24, NOW)).isEqualTo(NOW.plusHours(24));
    }

    @Test
    void rejectsMissingAmbiguousPastAndOverlongActivationExpiry() {
        assertPolicyError(null, null, "activation.expiry_required");
        assertPolicyError(NOW.plusHours(1), 1, "activation.expiry_ambiguous");
        assertPolicyError(NOW, null, "activation.expiry_invalid");
        assertPolicyError(NOW.plusDays(90).plusSeconds(1), null, "activation.expiry_too_long");
    }

    @Test
    void acceptsLongTermOrFutureEmployeeAccessAndRejectsPastValues() {
        assertThat(ValidityPolicy.accessExpiry(null, NOW)).isNull();
        assertThat(ValidityPolicy.accessExpiry(NOW.plusYears(1), NOW)).isEqualTo(NOW.plusYears(1));
        assertThatThrownBy(() -> ValidityPolicy.accessExpiry(NOW, NOW))
                .isInstanceOfSatisfying(ApiException.class,
                        exception -> assertThat(exception.code()).isEqualTo("employee.access_expiry_invalid"));
    }

    private static void assertPolicyError(OffsetDateTime expiresAt, Integer hours, String code) {
        assertThatThrownBy(() -> ValidityPolicy.activationCodeExpiry(expiresAt, hours, NOW))
                .isInstanceOfSatisfying(ApiException.class,
                        exception -> assertThat(exception.code()).isEqualTo(code));
    }
}
