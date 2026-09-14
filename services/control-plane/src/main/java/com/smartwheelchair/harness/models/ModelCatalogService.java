package com.smartwheelchair.harness.models;

import com.smartwheelchair.harness.activation.LiteLlmKeyIssuer;
import com.smartwheelchair.harness.api.ApiException;
import com.smartwheelchair.harness.api.ModelCatalogDtos;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.nio.charset.StandardCharsets;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

@Service
public class ModelCatalogService {
    private final NamedParameterJdbcTemplate jdbc;
    private final LiteLlmKeyIssuer keyIssuer;

    public ModelCatalogService(NamedParameterJdbcTemplate jdbc, LiteLlmKeyIssuer keyIssuer) {
        this.jdbc = jdbc;
        this.keyIssuer = keyIssuer;
    }

    public ModelCatalogDtos.PublicCatalog publicCatalog() {
        var enabled = adminCatalog().stream().filter(ModelCatalogDtos.AdminModel::enabled).toList();
        var defaultModel = enabled.stream().filter(ModelCatalogDtos.AdminModel::defaultModel)
                .map(ModelCatalogDtos.AdminModel::id).findFirst()
                .orElseThrow(() -> new ApiException("models.no_default", "公司当前没有可用的默认模型。", HttpStatus.SERVICE_UNAVAILABLE));
        return new ModelCatalogDtos.PublicCatalog(defaultModel, enabled.stream().map(ModelCatalogService::publicEntry).toList());
    }

    public List<ModelCatalogDtos.AdminModel> adminCatalog() {
        return jdbc.query("""
                        select model_id, display_name, description, enabled, experimental,
                               supports_image, default_model, sort_order, updated_at
                        from company_harness.model_catalog
                        order by sort_order, model_id
                        """, new MapSqlParameterSource(), (rs, row) -> new ModelCatalogDtos.AdminModel(
                rs.getString("model_id"), rs.getString("display_name"), rs.getString("description"),
                rs.getBoolean("enabled"), rs.getBoolean("experimental"),
                rs.getBoolean("supports_image") ? List.of("text", "image") : List.of("text"),
                rs.getBoolean("default_model"), rs.getInt("sort_order"),
                rs.getObject("updated_at", OffsetDateTime.class)));
    }

    public List<String> enabledModelIds() {
        return publicCatalog().models().stream().map(ModelCatalogDtos.ModelEntry::id).toList();
    }

    public List<String> keyModelIds() {
        var result = new ArrayList<>(enabledModelIds());
        if (result.contains("deepseek-v4-flash")) result.add("company-fast");
        if (result.contains("deepseek-v4-pro")) {
            result.add("company-coder");
            result.add("company-pro");
        }
        return List.copyOf(result);
    }

    @Transactional
    public ModelCatalogDtos.UpdateModelResponse update(String modelId, ModelCatalogDtos.UpdateModelRequest request, String actor) {
        var current = find(modelId, true);
        if (!request.enabled() && current.defaultModel()) {
            throw new ApiException("models.default_disable_forbidden", "请先将其他模型设为默认模型，再禁用当前默认模型。", HttpStatus.CONFLICT);
        }
        if (current.enabled() && !request.enabled() && enabledModelIds().size() == 1) {
            throw new ApiException("models.last_disable_forbidden", "至少需要保留一个可用模型。", HttpStatus.CONFLICT);
        }
        var targetDefault = request.defaultModel() || current.defaultModel();
        if (request.defaultModel()) {
            jdbc.update("update company_harness.model_catalog set default_model = false, updated_at = now() where default_model", new MapSqlParameterSource());
        }
        jdbc.update("""
                        update company_harness.model_catalog
                        set enabled = :enabled,
                            default_model = :defaultModel,
                            updated_at = now()
                        where model_id = :modelId
                        """, new MapSqlParameterSource()
                .addValue("enabled", request.enabled() || request.defaultModel())
                .addValue("defaultModel", targetDefault)
                .addValue("modelId", modelId));

        var tokens = activeTokenIds();
        keyIssuer.updateModels(tokens, keyModelIds());
        jdbc.update("""
                        insert into company_harness.admin_audit_events
                            (actor_admin_id, action, resource_type, resource_id, metadata)
                        values (:actor, 'models.update', 'model', :modelId,
                                jsonb_build_object('enabled', :enabled, 'defaultModel', :defaultModel,
                                                   'synchronizedVirtualKeyCount', :keyCount))
                        """, new MapSqlParameterSource()
                .addValue("actor", UUID.nameUUIDFromBytes(actor.getBytes(StandardCharsets.UTF_8)))
                .addValue("modelId", modelId)
                .addValue("enabled", request.enabled() || request.defaultModel())
                .addValue("defaultModel", targetDefault)
                .addValue("keyCount", tokens.size()));
        return new ModelCatalogDtos.UpdateModelResponse(find(modelId, false), tokens.size());
    }

    public int synchronizeActiveKeys() {
        var tokens = activeTokenIds();
        keyIssuer.updateModels(tokens, keyModelIds());
        return tokens.size();
    }

    private List<String> activeTokenIds() {
        return jdbc.queryForList("""
                        select litellm_token_id from company_harness.issued_virtual_keys
                        where status = 'active' order by created_at
                        """, new MapSqlParameterSource(), String.class);
    }

    private ModelCatalogDtos.AdminModel find(String modelId, boolean lock) {
        var rows = jdbc.query("""
                        select model_id, display_name, description, enabled, experimental,
                               supports_image, default_model, sort_order, updated_at
                        from company_harness.model_catalog where model_id = :modelId
                        """ + (lock ? " for update" : ""), new MapSqlParameterSource("modelId", modelId),
                (rs, row) -> new ModelCatalogDtos.AdminModel(
                        rs.getString("model_id"), rs.getString("display_name"), rs.getString("description"),
                        rs.getBoolean("enabled"), rs.getBoolean("experimental"),
                        rs.getBoolean("supports_image") ? List.of("text", "image") : List.of("text"),
                        rs.getBoolean("default_model"), rs.getInt("sort_order"),
                        rs.getObject("updated_at", OffsetDateTime.class)));
        if (rows.isEmpty()) throw new ApiException("models.not_found", "模型不存在。", HttpStatus.NOT_FOUND);
        return rows.getFirst();
    }

    private static ModelCatalogDtos.ModelEntry publicEntry(ModelCatalogDtos.AdminModel model) {
        return new ModelCatalogDtos.ModelEntry(model.id(), model.name(), model.description(), model.experimental(), model.inputModalities());
    }
}
