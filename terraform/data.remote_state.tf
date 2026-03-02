data "terraform_remote_state" "idp_core" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.idp_core_state.resource_group_name
    storage_account_name = var.idp_core_state.storage_account_name
    container_name       = var.idp_core_state.container_name
    key                  = var.idp_core_state.key
    use_oidc             = true
  }
}

data "terraform_remote_state" "platform_hosting" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.platform_hosting_state.resource_group_name
    storage_account_name = var.platform_hosting_state.storage_account_name
    container_name       = var.platform_hosting_state.container_name
    key                  = var.platform_hosting_state.key
    use_oidc             = true
  }
}
