package com.smartwheelchair.harness.api;

import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

import java.util.Map;

@RestController
public class HealthController {
    private final JdbcTemplate jdbc;

    public HealthController(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    @GetMapping("/health/live")
    Map<String, String> live() {
        return Map.of("status", "ok");
    }

    @GetMapping("/health/ready")
    Map<String, String> ready() {
        jdbc.queryForObject("select 1", Integer.class);
        return Map.of("status", "ready", "database", "ok");
    }
}
