# ---------------------------------------------------------------------------
# variables.tf — input variables for SIESA POS infrastructure
# ---------------------------------------------------------------------------

variable "project_id" {
  description = "GCP project ID"
  type        = string
}

variable "region" {
  description = "GCP region for all resources"
  type        = string
  default     = "us-central1"
}

variable "environment" {
  description = "Deployment environment label (production, staging)"
  type        = string
  default     = "production"

  validation {
    condition     = contains(["production", "staging"], var.environment)
    error_message = "environment must be 'production' or 'staging'."
  }
}

variable "db_tier" {
  description = "Cloud SQL machine tier"
  type        = string
  default     = "db-f1-micro"
}

variable "cloudhub_image" {
  description = "Full container image path for Cloud Run (e.g. us-central1-docker.pkg.dev/proj/pos-images/pos-cloudhub:v1)"
  type        = string
}

variable "min_instances" {
  description = "Minimum Cloud Run instances (keep >=1 to avoid cold starts)"
  type        = number
  default     = 1
}

variable "max_instances" {
  description = "Maximum Cloud Run instances for autoscaling"
  type        = number
  default     = 10
}

variable "alert_email" {
  description = "Email address for monitoring alerts"
  type        = string
  default     = ""
}
