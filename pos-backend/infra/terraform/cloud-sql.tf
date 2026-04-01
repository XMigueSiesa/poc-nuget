# ---------------------------------------------------------------------------
# cloud-sql.tf — Cloud SQL PostgreSQL 17 instance, database, user, and
#                private service connection
# ---------------------------------------------------------------------------

# Reserve an IP range for the private service connection
resource "google_compute_global_address" "private_ip_range" {
  name          = "pos-private-ip-range"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 16
  network       = google_compute_network.pos_vpc.id
}

# Establish the private service connection so Cloud SQL can use a private IP
resource "google_service_networking_connection" "private_vpc_connection" {
  network                 = google_compute_network.pos_vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_ip_range.name]

  depends_on = [google_compute_global_address.private_ip_range]
}

# Random password for the application database user
resource "random_password" "db_user_password" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

# Cloud SQL PostgreSQL 17 instance
resource "google_sql_database_instance" "pos" {
  name             = "pos-cloud-sql"
  database_version = "POSTGRES_17"
  region           = var.region

  settings {
    tier              = var.db_tier
    availability_type = "REGIONAL"
    disk_autoresize   = true
    disk_size         = 20
    disk_type         = "PD_SSD"

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
      transaction_log_retention_days = 7
      backup_retention_settings {
        retained_backups = 14
        retention_unit   = "COUNT"
      }
    }

    database_flags {
      name  = "log_min_duration_statement"
      value = "1000"
    }

    ip_configuration {
      ipv4_enabled    = false
      private_network = google_compute_network.pos_vpc.id
    }

    insights_config {
      query_insights_enabled  = true
      query_string_length     = 1024
      record_application_tags = true
      record_client_address   = false
    }
  }

  deletion_protection = true

  depends_on = [google_service_networking_connection.private_vpc_connection]
}

# Application database
resource "google_sql_database" "pos_cloud" {
  name     = "pos_cloud"
  instance = google_sql_database_instance.pos.name
}

# Application database user (no superuser privileges)
resource "google_sql_user" "pos_app" {
  name     = "pos_app"
  instance = google_sql_database_instance.pos.name
  password = random_password.db_user_password.result

  lifecycle {
    ignore_changes = [password]
  }
}
