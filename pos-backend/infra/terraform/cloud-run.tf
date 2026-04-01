# ---------------------------------------------------------------------------
# cloud-run.tf — CloudHub Cloud Run service
# ---------------------------------------------------------------------------

resource "google_cloud_run_v2_service" "cloudhub" {
  name     = "pos-cloudhub"
  location = var.region

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }

  template {
    service_account = google_service_account.cloudhub_sa.email

    scaling {
      min_instance_count = var.min_instances
      max_instance_count = var.max_instances
    }

    vpc_access {
      connector = google_vpc_access_connector.pos_connector.id
      egress    = "ALL_TRAFFIC"
    }

    containers {
      image = var.cloudhub_image

      ports {
        container_port = 8080
      }

      resources {
        limits = {
          cpu    = "2"
          memory = "1Gi"
        }
        cpu_idle = true
      }

      # Database connection string from Secret Manager
      env {
        name = "ConnectionStrings__CloudDb"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.db_connection_string.secret_id
            version = "latest"
          }
        }
      }

      # JWT signing key from Secret Manager
      env {
        name = "Auth__JwtSigningKey"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.jwt_signing_key.secret_id
            version = "latest"
          }
        }
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      liveness_probe {
        http_get {
          path = "/health/live"
        }
        initial_delay_seconds = 10
        period_seconds        = 30
        failure_threshold     = 3
        timeout_seconds       = 5
      }

      startup_probe {
        http_get {
          path = "/health/ready"
        }
        initial_delay_seconds = 5
        period_seconds        = 10
        failure_threshold     = 3
        timeout_seconds       = 5
      }
    }
  }

  depends_on = [
    google_vpc_access_connector.pos_connector,
    google_secret_manager_secret_version.db_connection_string_placeholder,
    google_secret_manager_secret_version.jwt_signing_key_initial,
    google_secret_manager_secret_iam_member.cloudhub_sa_db_connection_string,
    google_secret_manager_secret_iam_member.cloudhub_sa_jwt_signing_key,
  ]
}

# Allow unauthenticated invocations — auth is handled at the app layer via JWT
resource "google_cloud_run_v2_service_iam_member" "cloudhub_public_invoker" {
  project  = google_cloud_run_v2_service.cloudhub.project
  location = google_cloud_run_v2_service.cloudhub.location
  name     = google_cloud_run_v2_service.cloudhub.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}
