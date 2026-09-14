package com.smartwheelchair.harness.admin;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

import java.util.Map;

@Component
public final class EmployeeAccessExpiryScheduler {
    private static final Logger LOGGER = LoggerFactory.getLogger(EmployeeAccessExpiryScheduler.class);
    private final NamedParameterJdbcTemplate jdbc;
    private final AdminService adminService;

    public EmployeeAccessExpiryScheduler(NamedParameterJdbcTemplate jdbc, AdminService adminService) {
        this.jdbc = jdbc;
        this.adminService = adminService;
    }

    @Scheduled(
            initialDelayString = "${harness.access-expiry.initial-delay-ms:15000}",
            fixedDelayString = "${harness.access-expiry.sweep-ms:60000}")
    public void disableExpiredEmployees() {
        var employeeNumbers = jdbc.queryForList("""
                        select employee_number
                        from company_harness.employees
                        where status = 'active'
                          and access_expires_at is not null
                          and access_expires_at <= now()
                        order by access_expires_at
                        limit 100
                        """,
                Map.of(),
                String.class);
        var disabled = 0;
        for (var employeeNumber : employeeNumbers) {
            try {
                if (adminService.expireEmployeeAccess(employeeNumber)) {
                    disabled++;
                }
            } catch (RuntimeException exception) {
                LOGGER.error("Unable to revoke access for one expired employee; it will be retried.", exception);
            }
        }
        if (disabled > 0) {
            LOGGER.info("Disabled {} employee account(s) after access expiry.", disabled);
        }
    }
}
