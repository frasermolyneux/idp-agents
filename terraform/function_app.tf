resource "azurerm_linux_function_app" "function_app" {
  name = local.function_app_name

  resource_group_name = data.azurerm_resource_group.rg.name
  location            = data.azurerm_resource_group.rg.location

  service_plan_id = data.azurerm_service_plan.sp.id

  storage_account_name          = azurerm_storage_account.function_app_storage.name
  storage_uses_managed_identity = true
  https_only                    = true
  functions_extension_version   = "~4"

  identity {
    type         = "UserAssigned"
    identity_ids = [local.agents_identity_id]
  }

  key_vault_reference_identity_id = local.agents_identity_id

  site_config {
    application_stack {
      use_dotnet_isolated_runtime = true
      dotnet_version              = "9.0"
    }

    application_insights_connection_string = local.app_insights_connection_string
    application_insights_key               = local.app_insights_instrumentation_key

    ftps_state          = "Disabled"
    always_on           = true
    minimum_tls_version = "1.2"

    health_check_path                 = "/api/health"
    health_check_eviction_time_in_min = 5
  }

  auth_settings_v2 {
    auth_enabled           = true
    require_authentication = true

    active_directory_v2 {
      client_id            = local.idp_agents_app_client_id
      tenant_auth_endpoint = "https://login.microsoftonline.com/${data.azurerm_client_config.current.tenant_id}/v2.0"
      allowed_audiences = [
        format("api://%s/idp-agents-%s", data.azurerm_client_config.current.tenant_id, var.environment),
        local.idp_agents_app_client_id
      ]
    }

    login {}
  }

  app_settings = {
    "AZURE_CLIENT_ID"             = local.agents_identity_client_id
    "AzureAd__Instance"           = "https://login.microsoftonline.com/"
    "AzureAd__TenantId"           = data.azurerm_client_config.current.tenant_id
    "AzureAd__ClientId"           = local.idp_agents_app_client_id
    "AzureAd__Audience"           = format("api://%s/idp-agents-%s", data.azurerm_client_config.current.tenant_id, var.environment)
    "AzureOpenAI__Endpoint"             = local.openai_endpoint
    "AzureOpenAI__ChatDeployment"       = "gpt-4.1-mini"
    "AzureOpenAI__EmbeddingDeployment"  = "text-embedding-ada-002"
    "AzureSearch__Endpoint"             = local.ai_search_endpoint
    "KnowledgeStorage__blobServiceUri"  = local.knowledge_storage_endpoint
    "KnowledgeStorage__queueServiceUri" = local.knowledge_storage_queue_endpoint
    "KeyVault__Uri"               = local.key_vault_uri
    "CosmosDb__Endpoint"          = local.cosmosdb_endpoint
    "GitHubApp__AppId"            = local.github_app_id
    "GitHubApp__InstallationId"   = local.github_app_installation_id
    "GitHubApp__PemSecretName"    = local.github_app_pem_secret_name
  }

  lifecycle {
    ignore_changes = [app_settings["WEBSITE_RUN_FROM_PACKAGE"]]
  }
}
