package com.smartwheelchair.harness;

import com.smartwheelchair.harness.config.ActivationProperties;
import com.smartwheelchair.harness.config.AdminProperties;
import com.smartwheelchair.harness.config.GatewayProperties;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.scheduling.annotation.EnableScheduling;

@SpringBootApplication
@EnableScheduling
@EnableConfigurationProperties({ActivationProperties.class, GatewayProperties.class, AdminProperties.class})
public class ControlPlaneApplication {
    public static void main(String[] args) {
        SpringApplication.run(ControlPlaneApplication.class, args);
    }
}
