create table company_harness.model_catalog (
  model_id text primary key,
  display_name text not null,
  description text not null,
  enabled boolean not null default true,
  experimental boolean not null default false,
  supports_image boolean not null default false,
  default_model boolean not null default false,
  sort_order integer not null,
  updated_at timestamptz not null default now()
);

create unique index model_catalog_one_default_idx
  on company_harness.model_catalog ((default_model)) where default_model;

insert into company_harness.model_catalog
  (model_id, display_name, description, enabled, experimental, supports_image, default_model, sort_order)
values
  ('deepseek-v4-flash', 'DeepSeek V4 Flash', '低延迟通用模型，适合日常问答、资料整理和并行任务。', true, false, false, true, 10),
  ('deepseek-v4-pro', 'DeepSeek V4 Pro', '质量优先模型，适合编程、复杂推理和高质量交付。', true, false, false, false, 20),
  ('deepseek-v4-flash-vision-exp', 'DeepSeek V4 Flash Vision Exp', '支持文本和图片输入的实验模型。', true, true, true, false, 30);
