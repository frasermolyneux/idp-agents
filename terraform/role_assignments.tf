resource "azurerm_role_assignment" "function_app_storage_blob_owner" {
  scope                = azurerm_storage_account.function_app_storage.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = local.agents_identity_principal_id
}

resource "azurerm_role_assignment" "function_app_storage_account_contributor" {
  scope                = azurerm_storage_account.function_app_storage.id
  role_definition_name = "Storage Account Contributor"
  principal_id         = local.agents_identity_principal_id
}
