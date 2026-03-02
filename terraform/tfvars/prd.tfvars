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

platform_workloads_state = {
  resource_group_name  = "rg-tf-platform-workloads-prd-uksouth-01"
  storage_account_name = "sadz9ita659lj9xb3"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
  subscription_id      = "7760848c-794d-4a19-8cb2-52f71a21ac2b"
  tenant_id            = "e56a6947-bb9a-4a6e-846a-1f118d1c3a14"
}
