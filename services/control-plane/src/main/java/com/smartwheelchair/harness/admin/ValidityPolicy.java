package com.smartwheelchair.harness.admin;

import com.smartwheelchair.harness.api.ApiException;
import org.springframework.http.HttpStatus;

import java.time.OffsetDateTime;

final class ValidityPolicy {
    static final int MAX_ACTIVATION_CODE_HOURS = 90 * 24;

    private ValidityPolicy() {
    }

    static OffsetDateTime activationCodeExpiry(
            OffsetDateTime requestedExpiresAt,
            Integer legacyExpiresInHours,
            OffsetDateTime now) {
        if (requestedExpiresAt != null && legacyExpiresInHours != null) {
            throw badRequest("activation.expiry_ambiguous", "请只提交 expiresAt，不要同时提交 expiresInHours。");
        }

        var expiresAt = requestedExpiresAt;
        if (expiresAt == null && legacyExpiresInHours != null) {
            expiresAt = now.plusHours(legacyExpiresInHours);
        }
        if (expiresAt == null) {
            throw badRequest("activation.expiry_required", "请选择激活码有效期。");
        }
        if (!expiresAt.isAfter(now)) {
            throw badRequest("activation.expiry_invalid", "激活码失效时间必须晚于当前时间。");
        }
        if (expiresAt.isAfter(now.plusHours(MAX_ACTIVATION_CODE_HOURS))) {
            throw badRequest("activation.expiry_too_long", "激活码有效期最长为 90 天。");
        }
        return expiresAt;
    }

    static OffsetDateTime accessExpiry(OffsetDateTime accessExpiresAt, OffsetDateTime now) {
        if (accessExpiresAt != null && !accessExpiresAt.isAfter(now)) {
            throw badRequest("employee.access_expiry_invalid", "员工访问结束时间必须晚于当前时间。");
        }
        return accessExpiresAt;
    }

    private static ApiException badRequest(String code, String message) {
        return new ApiException(code, message, HttpStatus.BAD_REQUEST);
    }
}
