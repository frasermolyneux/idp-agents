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
