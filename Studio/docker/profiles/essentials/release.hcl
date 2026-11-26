variable "STUDIO_VERSION" {
  default = ""
}

variable "STUDIO_PROFILE_CONTEXT" {
  default = ""
}

variable "STUDIO_PROFILE_TAG" {
  default = ""
}

variable "STUDIO_PROFILE_PLATFORMS" {
  default = "linux/amd64"
}

target "studio-foundation" {
  context    = "."
  dockerfile = "Studio/src/apps/Elsa.Studio.Foundation.Host/Dockerfile"
  args       = { STUDIO_VERSION = STUDIO_VERSION }
  platforms  = split(",", STUDIO_PROFILE_PLATFORMS)
}

target "profile" {
  context    = STUDIO_PROFILE_CONTEXT
  dockerfile = "Dockerfile"
  tags       = [STUDIO_PROFILE_TAG]
  network    = "none"
  contexts   = { studio-foundation = "target:studio-foundation" }
  args       = { FOUNDATION_IMAGE = "studio-foundation" }
  platforms  = split(",", STUDIO_PROFILE_PLATFORMS)
}
