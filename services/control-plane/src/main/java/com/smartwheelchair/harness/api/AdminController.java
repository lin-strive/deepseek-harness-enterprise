package com.smartwheelchair.harness.api;

import com.smartwheelchair.harness.admin.AdminService;
import com.smartwheelchair.harness.models.ModelCatalogService;
import jakarta.validation.Valid;
import org.springframework.http.MediaType;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestPart;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.multipart.MultipartFile;

import java.util.List;

@RestController
@RequestMapping("/api/v1/admin")
public class AdminController {
    private final AdminService service;
    private final ModelCatalogService modelCatalogService;

    public AdminController(AdminService service, ModelCatalogService modelCatalogService) {
        this.service = service;
        this.modelCatalogService = modelCatalogService;
    }

    @GetMapping("/models")
    List<ModelCatalogDtos.AdminModel> models() {
        return modelCatalogService.adminCatalog();
    }

    @PutMapping("/models/{modelId}")
    ModelCatalogDtos.UpdateModelResponse updateModel(
            @PathVariable String modelId,
            @Valid @RequestBody ModelCatalogDtos.UpdateModelRequest request,
            Authentication authentication) {
        return modelCatalogService.update(modelId, request, authentication.getName());
    }

    @PostMapping("/models/synchronize")
    java.util.Map<String, Integer> synchronizeModels() {
        return java.util.Map.of("synchronizedVirtualKeyCount", modelCatalogService.synchronizeActiveKeys());
    }

    @GetMapping("/overview")
    AdminDtos.OverviewResponse overview() {
        return service.overview();
    }

    @GetMapping("/employees")
    List<AdminDtos.EmployeeRow> employees() {
        return service.employees();
    }

    @PostMapping("/employees")
    ResponseEntity<AdminDtos.EmployeeRow> createEmployee(
            @Valid @RequestBody AdminDtos.CreateEmployeeRequest request,
            Authentication authentication) {
        return ResponseEntity.status(HttpStatus.CREATED)
                .body(service.createEmployee(request, authentication.getName()));
    }

    @PutMapping("/employees/{employeeNumber}/status")
    AdminDtos.EmployeeMutationResponse changeEmployeeStatus(
            @PathVariable String employeeNumber,
            @Valid @RequestBody AdminDtos.EmployeeStatusRequest request,
            Authentication authentication) {
        return service.changeEmployeeStatus(employeeNumber, request, authentication.getName());
    }

    @PutMapping("/employees/{employeeNumber}/access-expiry")
    AdminDtos.EmployeeRow changeEmployeeAccessExpiry(
            @PathVariable String employeeNumber,
            @Valid @RequestBody AdminDtos.EmployeeAccessExpiryRequest request,
            Authentication authentication) {
        return service.changeEmployeeAccessExpiry(employeeNumber, request, authentication.getName());
    }

    @DeleteMapping("/employees/{employeeNumber}")
    ResponseEntity<Void> deleteEmployee(
            @PathVariable String employeeNumber,
            Authentication authentication) {
        service.deleteEmployee(employeeNumber, authentication.getName());
        return ResponseEntity.noContent().build();
    }

    @PostMapping(path = "/employees/import", consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    AdminDtos.ImportResponse importEmployees(
            @RequestPart("file") MultipartFile file,
            Authentication authentication) {
        return service.importEmployees(file, authentication.getName());
    }

    @PostMapping("/activation-codes/batch")
    AdminDtos.BatchActivationResponse generateCodes(
            @Valid @RequestBody AdminDtos.BatchActivationRequest request,
            Authentication authentication) {
        return service.generateCodes(request, authentication.getName());
    }
}
