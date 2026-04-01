# ---------------------------------------------------------------------------
# main.tf — Provider and backend configuration
# ---------------------------------------------------------------------------

terraform {
  required_version = ">= 1.6"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  backend "gcs" {
    bucket = "siesa-pos-terraform-state"
    prefix = "pos-backend"
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}
