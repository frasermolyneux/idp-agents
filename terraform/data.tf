data "azurerm_resource_group" "rg" {
  name = local.resource_group_name
}

data "azurerm_service_plan" "sp" {
  name                = local.app_service_plan.name
  resource_group_name = local.app_service_plan.resource_group_name
}

data "azurerm_client_config" "current" {}
