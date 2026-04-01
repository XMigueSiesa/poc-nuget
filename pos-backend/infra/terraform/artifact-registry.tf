# ---------------------------------------------------------------------------
# artifact-registry.tf — Docker repository for POS container images
# ---------------------------------------------------------------------------

resource "google_artifact_registry_repository" "pos_images" {
  location      = var.region
  repository_id = "pos-images"
  format        = "DOCKER"
  description   = "Container images for SIESA POS services"

  labels = {
    environment = var.environment
    managed_by  = "terraform"
  }

  cleanup_policies {
    id     = "keep-last-10"
    action = "KEEP"

    most_recent_versions {
      keep_count = 10
    }
  }

  cleanup_policies {
    id     = "delete-older"
    action = "DELETE"

    condition {
      older_than = "2592000s" # 30 days
    }
  }
}
