locals {
  workload_resource_group = data.terraform_remote_state.platform_workloads.outputs.workload_resource_groups[var.workload_name][var.environment].resource_groups[lower(var.location)]
  resource_group_name     = local.workload_resource_group.name

  app_service_plan = data.terraform_remote_state.platform_hosting.outputs.app_service_plans["default"]

  agents_identity_id           = data.terraform_remote_state.idp_core.outputs.idp_agents_mi_id
  agents_identity_client_id    = data.terraform_remote_state.idp_core.outputs.idp_agents_mi_client_id
  agents_identity_principal_id = data.terraform_remote_state.idp_core.outputs.idp_agents_mi_principal_id

  ai_search_endpoint          = data.terraform_remote_state.idp_core.outputs.ai_search_endpoint
  knowledge_storage_endpoint  = data.terraform_remote_state.idp_core.outputs.knowledge_storage_endpoint

  app_insights_connection_string   = data.terraform_remote_state.idp_core.outputs.app_insights_connection_string
  app_insights_instrumentation_key = data.terraform_remote_state.idp_core.outputs.app_insights_instrumentation_key

  openai_endpoint   = data.terraform_remote_state.idp_core.outputs.openai_endpoint
  key_vault_uri     = data.terraform_remote_state.idp_core.outputs.key_vault_uri
  cosmosdb_endpoint = data.terraform_remote_state.idp_core.outputs.cosmosdb_endpoint
  dts_endpoint      = data.terraform_remote_state.idp_core.outputs.dts_endpoint
  dts_task_hub_name = data.terraform_remote_state.idp_core.outputs.dts_task_hub_name

  github_app_id              = data.terraform_remote_state.idp_core.outputs.github_app_id
  github_app_installation_id = data.terraform_remote_state.idp_core.outputs.github_app_installation_id
  github_app_pem_secret_name = data.terraform_remote_state.idp_core.outputs.github_app_pem_secret_name

  idp_agents_app_client_id = data.terraform_remote_state.idp_core.outputs.idp_agents_app_client_id

  function_app_name         = format("fn-idp-agents-%s-%s", var.environment, var.location)
  function_app_storage_name = format("sa%s", replace(format("fn-idp-agents-%s", var.environment), "-", ""))
}
