package com.smartwheelchair.harness.api;

import com.smartwheelchair.harness.activation.ActivationService;
import jakarta.validation.Valid;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/v1/activation")
public class ActivationController {
    private final ActivationService service;

    public ActivationController(ActivationService service) {
        this.service = service;
    }

    @PostMapping("/preview")
    ActivationDtos.PreviewResponse preview(@Valid @RequestBody ActivationDtos.PreviewRequest request) {
        return service.preview(request);
    }

    @PostMapping("/confirm")
    ActivationDtos.ConfirmResponse confirm(@Valid @RequestBody ActivationDtos.ConfirmRequest request) {
        return service.confirm(request);
    }
}
