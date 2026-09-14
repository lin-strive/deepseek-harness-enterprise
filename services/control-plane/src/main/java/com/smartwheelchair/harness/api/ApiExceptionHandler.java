package com.smartwheelchair.harness.api;

import jakarta.validation.ConstraintViolationException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.multipart.MaxUploadSizeExceededException;

import java.util.UUID;

@RestControllerAdvice
public class ApiExceptionHandler {
    private static final Logger LOGGER = LoggerFactory.getLogger(ApiExceptionHandler.class);

    @ExceptionHandler(ApiException.class)
    ResponseEntity<ActivationDtos.ApiError> handleApi(ApiException exception) {
        return error(exception.status(), exception.code(), exception.getMessage());
    }

    @ExceptionHandler(MethodArgumentNotValidException.class)
    ResponseEntity<ActivationDtos.ApiError> handleValidation(MethodArgumentNotValidException exception) {
        var fieldError = exception.getBindingResult().getFieldError();
        var message = fieldError == null ? "请求格式或字段内容无效。" : fieldError.getDefaultMessage();
        return error(HttpStatus.BAD_REQUEST, "request.invalid", message);
    }

    @ExceptionHandler(ConstraintViolationException.class)
    ResponseEntity<ActivationDtos.ApiError> handleConstraintViolation(ConstraintViolationException exception) {
        return error(HttpStatus.BAD_REQUEST, "request.invalid", "请求格式或字段内容无效。");
    }

    @ExceptionHandler(HttpMessageNotReadableException.class)
    ResponseEntity<ActivationDtos.ApiError> handleUnreadableMessage(HttpMessageNotReadableException exception) {
        return error(
                HttpStatus.BAD_REQUEST,
                "request.invalid_number",
                "请求中的数值格式错误或超出允许范围，请按字段提示修改后重试。");
    }

    @ExceptionHandler(MaxUploadSizeExceededException.class)
    ResponseEntity<ActivationDtos.ApiError> handleUploadSize(MaxUploadSizeExceededException exception) {
        return error(HttpStatus.CONTENT_TOO_LARGE, "csv.too_large", "CSV 文件不能超过 2 MB。");
    }

    @ExceptionHandler(Exception.class)
    ResponseEntity<ActivationDtos.ApiError> handleUnexpected(Exception exception) {
        var supportId = UUID.randomUUID().toString();
        LOGGER.error("Unhandled control-plane error. supportId={}", supportId, exception);
        return ResponseEntity.status(HttpStatus.INTERNAL_SERVER_ERROR)
                .body(new ActivationDtos.ApiError("server.error", "服务暂时不可用，请稍后重试。", supportId));
    }

    private static ResponseEntity<ActivationDtos.ApiError> error(HttpStatus status, String code, String message) {
        return ResponseEntity.status(status)
                .body(new ActivationDtos.ApiError(code, message, UUID.randomUUID().toString()));
    }
}
