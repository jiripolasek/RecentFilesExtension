@{
    # Optional: copy to HyperV.config.psd1 for settings that differ per VM.
    # Paths are relative to this configuration file.
    PackageConfigPath = 'Package.config.psd1'

    # Set here or use MSIX_HYPERV_CERTIFICATE_THUMBPRINT. See docs/dev/HyperVDeployment.md.
    CertificateThumbprint = ''
    # Omit optional values to use MSIX_HYPERV_* environment defaults.
    # CertificateStoreLocation = 'CurrentUser' # CurrentUser or LocalMachine

    # GuestPackageDirectory = 'C:\ProgramData\MSIXDeployments'

    VirtualMachines = @(
        @{
            Name = 'REPLACE_WITH_VM_NAME'
            # Platform defaults to MSIX_HYPERV_PLATFORM, then Package.config.psd1's DefaultPlatform.
            # Platform = 'x64'
            # UserName defaults to MSIX_HYPERV_GUEST_USERNAME or is prompted for.
            # UserName = 'YourGuestAccount'
            # Used only when -RestoreCheckpoint is explicitly passed.
            # CheckpointName = 'Your clean checkpoint'
        }
    )
}
