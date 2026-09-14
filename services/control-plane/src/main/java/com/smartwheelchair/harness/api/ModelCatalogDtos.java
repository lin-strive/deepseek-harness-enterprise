package com.smartwheelchair.harness.api;

import jakarta.validation.constraints.NotNull;

import java.time.OffsetDateTime;
import java.util.List;

public final class ModelCatalogDtos {
    private ModelCatalogDtos() {
    }

    public record ModelEntry(
            String id,
            String name,
            String description,
            boolean experimental,
            List<String> inputModalities) {
    }

    public record PublicCatalog(String defaultModel, List<ModelEntry> models) {
    }

    public record AdminModel(
            String id,
            String name,
            String description,
            boolean enabled,
            boolean experimental,
            List<String> inputModalities,
            boolean defaultModel,
            int sortOrder,
            OffsetDateTime updatedAt) {
    }

    public record UpdateModelRequest(@NotNull Boolean enabled, @NotNull Boolean defaultModel) {
    }

    public record UpdateModelResponse(AdminModel model, int synchronizedVirtualKeyCount) {
    }
}
