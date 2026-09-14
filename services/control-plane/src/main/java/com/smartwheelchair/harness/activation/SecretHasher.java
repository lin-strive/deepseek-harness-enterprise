package com.smartwheelchair.harness.activation;

import com.smartwheelchair.harness.config.ActivationProperties;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Component;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.HexFormat;
import java.util.Locale;

@Component
public final class SecretHasher {
    private final byte[] pepper;

    @Autowired
    public SecretHasher(ActivationProperties properties) {
        this(properties.pepper());
    }

    SecretHasher(String pepper) {
        if (pepper == null || pepper.getBytes(StandardCharsets.UTF_8).length < 32) {
            throw new IllegalStateException("Activation pepper must be at least 32 bytes.");
        }
        this.pepper = pepper.getBytes(StandardCharsets.UTF_8);
    }

    public String activationCode(String code) {
        try {
            var hmac = Mac.getInstance("HmacSHA256");
            hmac.init(new SecretKeySpec(pepper, "HmacSHA256"));
            return HexFormat.of().withUpperCase().formatHex(
                    hmac.doFinal(code.trim().toUpperCase(Locale.ROOT).getBytes(StandardCharsets.UTF_8)));
        } catch (GeneralSecurityException exception) {
            throw new IllegalStateException("HMAC-SHA-256 is unavailable.", exception);
        }
    }

    public String session(String sessionId) {
        try {
            return HexFormat.of().withUpperCase().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(sessionId.getBytes(StandardCharsets.UTF_8)));
        } catch (GeneralSecurityException exception) {
            throw new IllegalStateException("SHA-256 is unavailable.", exception);
        }
    }
}
