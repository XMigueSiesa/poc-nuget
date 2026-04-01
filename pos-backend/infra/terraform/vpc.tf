# ---------------------------------------------------------------------------
# vpc.tf — VPC, subnet, and Serverless VPC Access connector
# ---------------------------------------------------------------------------

resource "google_compute_network" "pos_vpc" {
  name                    = "pos-vpc"
  auto_create_subnetworks = false
  description             = "SIESA POS private network"
}

resource "google_compute_subnetwork" "pos_subnet" {
  name          = "pos-subnet"
  region        = var.region
  network       = google_compute_network.pos_vpc.id
  ip_cidr_range = "10.8.0.0/24"

  private_ip_google_access = true
}

# Serverless VPC Access connector — allows Cloud Run to reach Cloud SQL via private IP
resource "google_vpc_access_connector" "pos_connector" {
  name   = "pos-connector"
  region = var.region

  subnet {
    name = google_compute_subnetwork.pos_subnet.name
  }

  ip_cidr_range  = "10.8.1.0/28"
  min_throughput = 200
  max_throughput = 1000

  depends_on = [google_compute_subnetwork.pos_subnet]
}
