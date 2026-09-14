package com.smartwheelchair.harness.api;

import com.smartwheelchair.harness.models.ModelCatalogService;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/v1/models")
public class ModelCatalogController {
    private final ModelCatalogService service;

    public ModelCatalogController(ModelCatalogService service) {
        this.service = service;
    }

    @GetMapping
    ModelCatalogDtos.PublicCatalog catalog() {
        return service.publicCatalog();
    }
}
