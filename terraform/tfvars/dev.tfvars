environment = "dev"
location    = "swedencentral"
instance    = "01"

subscription_id = "6cad03c1-9e98-4160-8ebe-64dd30f1bbc7"

idp_core_state = {
  resource_group_name  = "rg-tf-idp-core-dev-uksouth-01"
  storage_account_name = "sa537ff6e2bfe3"
  container_name       = "tfstate"
  key                  = "terraform.tfstate"
}

platform_hosting_state = {
  resource_group_name  = "rg-tf-platform-hosting-dev-uksouth-01"
  storage_account_name = "saa3efe8753ccf"
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
