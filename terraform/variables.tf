variable "environment" {
  type = string
}

variable "location" {
  type    = string
  default = "swedencentral"
}

variable "instance" {
  type    = string
  default = "01"
}

variable "subscription_id" {
  type = string
}

variable "idp_core_state" {
  type = object({
    resource_group_name  = string
    storage_account_name = string
    container_name       = string
    key                  = string
  })
}

variable "platform_hosting_state" {
  type = object({
    resource_group_name  = string
    storage_account_name = string
    container_name       = string
    key                  = string
  })
}
