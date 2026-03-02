environment = "prd"
location    = "swedencentral"
instance    = "01"

subscription_id = "903b6685-c12a-4703-ac54-7ec1ff15ca43"

idp_core_state = {
  resource_group_name  = "rg-tf-idp-core-prd-uksouth-01"
  storage_account_name = "sae5a6da1fa728"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
}

platform_hosting_state = {
  resource_group_name  = "rg-tf-platform-hosting-prd-uksouth-01"
  storage_account_name = "sab227d365059d"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
}
