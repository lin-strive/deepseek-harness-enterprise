package com.smartwheelchair.harness.api;

import jakarta.validation.Validation;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpStatus;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.mock.http.MockHttpInputMessage;

import java.math.BigDecimal;

import static org.assertj.core.api.Assertions.assertThat;

class AdminRequestValidationTest {
    @Test
    void rejectsQuotasAboveCurrentGatewayLimits() {
        try (var factory = Validation.buildDefaultValidatorFactory()) {
            var request = new AdminDtos.CreateEmployeeRequest(
                    "SW001",
                    "张三",
                    "研发部",
                    "active",
                    null,
                    new BigDecimal("72000.01"),
                    601,
                    2_000_001,
                    21);

            var messages = factory.getValidator().validate(request).stream()
                    .map(violation -> violation.getMessage())
                    .toList();

            assertThat(messages).containsExactlyInAnyOrder(
                    "月度金额不能超过 72000 元。",
                    "RPM 不能超过 600。",
                    "TPM 不能超过 2000000。",
                    "并发请求数不能超过 20。");
        }
    }

    @Test
    void reportsUnreadableOrOverflowingNumbersAsBadRequest() {
        var handler = new ApiExceptionHandler();
        var exception = new HttpMessageNotReadableException(
                "numeric overflow",
                new MockHttpInputMessage(new byte[0]));

        var response = handler.handleUnreadableMessage(exception);

        assertThat(response.getStatusCode()).isEqualTo(HttpStatus.BAD_REQUEST);
        assertThat(response.getBody()).isNotNull();
        assertThat(response.getBody().code()).isEqualTo("request.invalid_number");
        assertThat(response.getBody().message()).contains("数值格式错误或超出允许范围");
    }
}
