# ---------------------------------------------------------------------------
# vpc.tf — VPC, subnet, VPC Access Connector, private service networking
# ---------------------------------------------------------------------------

resource "google_compute_network" "pos_vpc" {
  name                    = "pos-vpc"
  auto_create_subnetworks = false
}

resource "google_compute_subnetwork" "pos_subnet" {
  name          = "pos-subnet"
  ip_cidr_range = "10.0.0.0/24"
  region        = var.region
  network       = google_compute_network.pos_vpc.id

  private_ip_google_access = true
}

# VPC Access Connector — allows Cloud Run to reach private resources
resource "google_vpc_access_connector" "pos_connector" {
  name          = "pos-vpc-connector"
  region        = var.region
  ip_cidr_range = "10.8.0.0/28"
  network       = google_compute_network.pos_vpc.name

  min_instances = 2
  max_instances = 3
}

# Private service networking — Cloud SQL private IP
resource "google_compute_global_address" "private_ip_range" {
  name          = "pos-private-ip-range"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 16
  network       = google_compute_network.pos_vpc.id
}

resource "google_service_networking_connection" "private_vpc_connection" {
  network                 = google_compute_network.pos_vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_ip_range.name]
}
