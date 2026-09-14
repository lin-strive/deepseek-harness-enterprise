package com.smartwheelchair.harness.activation;

import com.smartwheelchair.harness.api.ActivationDtos;
import com.smartwheelchair.harness.config.GatewayProperties;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpMethod;
import org.springframework.http.MediaType;
import org.springframework.test.web.client.MockRestServiceServer;
import org.springframework.web.client.RestClient;

import java.math.BigDecimal;
import java.net.URI;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.hamcrest.Matchers.allOf;
import static org.hamcrest.Matchers.containsString;
import static org.springframework.test.web.client.ExpectedCount.once;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.content;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.header;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.method;
import static org.springframework.test.web.client.match.MockRestRequestMatchers.requestTo;
import static org.springframework.test.web.client.response.MockRestResponseCreators.withSuccess;

class LiteLlmKeyIssuerTest {
    @Test
    void sendsQuotaAndMetadataToLiteLlmAndReadsIssuedKey() {
        var builder = RestClient.builder();
        var server = MockRestServiceServer.bindTo(builder).build();
        var baseUrl = URI.create("http://litellm.test");
        var properties = new GatewayProperties(
                baseUrl,
                URI.create("http://gateway.example/v1"),
                "master-key",
                new BigDecimal("7.2"),
                List.of("deepseek-chat"));
        var issuer = new LiteLlmKeyIssuer(builder, properties);
        var employee = new ActivationDtos.EmployeeProfile(
                UUID.fromString("5e940494-3c52-4a85-8882-ab89723a87d2"),
                "SW001",
                "张三",
                "研发部");
        var quota = new ActivationDtos.QuotaSummary(
                new BigDecimal("720"), BigDecimal.ZERO, 30, 100000, 3, OffsetDateTime.now().plusMonths(1));

        server.expect(once(), requestTo("http://litellm.test/key/generate"))
                .andExpect(method(HttpMethod.POST))
                .andExpect(header("Authorization", "Bearer master-key"))
                .andExpect(content().string(allOf(
                        containsString("\"user_id\":\"5e9404943c524a858882ab89723a87d2\""),
                        containsString("\"key_alias\":\"employee-SW001\""),
                        containsString("\"max_budget\":100.0000"),
                        containsString("\"rpm_limit\":30"),
                        containsString("\"tpm_limit\":100000"),
                        containsString("\"max_parallel_requests\":3"),
                        containsString("\"department\":\"研发部\""))))
                .andRespond(withSuccess(
                        "{\"key\":\"sk-issued\",\"token_id\":\"token-123\"}",
                        MediaType.APPLICATION_JSON));

        var issued = issuer.issue(employee, quota, List.of("deepseek-v4-flash"));

        assertThat(issued.secret()).isEqualTo("sk-issued");
        assertThat(issued.tokenId()).isEqualTo("token-123");
        server.verify();
    }

    @Test
    void deletesIssuedTokenIdsWhenEmployeeIsDisabled() {
        var builder = RestClient.builder();
        var server = MockRestServiceServer.bindTo(builder).build();
        var properties = new GatewayProperties(
                URI.create("http://litellm.test"),
                URI.create("http://gateway.example/v1"),
                "master-key",
                new BigDecimal("7.2"),
                List.of("company-fast"));
        var issuer = new LiteLlmKeyIssuer(builder, properties);

        server.expect(once(), requestTo("http://litellm.test/key/delete"))
                .andExpect(method(HttpMethod.POST))
                .andExpect(header("Authorization", "Bearer master-key"))
                .andExpect(content().json("{\"keys\":[\"token-123\",\"token-456\"]}"))
                .andRespond(withSuccess("{\"deleted_keys\":[\"token-123\",\"token-456\"]}", MediaType.APPLICATION_JSON));

        issuer.revoke(List.of("token-123", "token-456"));

        server.verify();
    }

    @Test
    void updatesModelsForEveryActiveTokenId() {
        var builder = RestClient.builder();
        var server = MockRestServiceServer.bindTo(builder).build();
        var properties = new GatewayProperties(
                URI.create("http://litellm.test"),
                URI.create("http://gateway.example/v1"),
                "master-key",
                new BigDecimal("7.2"),
                List.of("deepseek-v4-flash"));
        var issuer = new LiteLlmKeyIssuer(builder, properties);

        for (var tokenId : List.of("token-123", "token-456")) {
            server.expect(once(), requestTo("http://litellm.test/key/update"))
                    .andExpect(method(HttpMethod.POST))
                    .andExpect(header("Authorization", "Bearer master-key"))
                    .andExpect(content().json("""
                            {"key":"%s","models":["deepseek-v4-flash","company-fast"]}
                            """.formatted(tokenId)))
                    .andRespond(withSuccess("{}", MediaType.APPLICATION_JSON));
        }

        issuer.updateModels(
                List.of("token-123", "token-456"),
                List.of("deepseek-v4-flash", "company-fast"));

        server.verify();
    }
}
