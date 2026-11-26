#
# Module manifest for module 'SibsCSV'
#

@{
RootModule = 'SibsCSV.dll'
ModuleVersion = '1.0.0'
CompatiblePSEditions = @("Core")
GUID = 'f0604aa9-26ba-481b-8c25-136f99468e6d'
Author = 'I.E.Sazonov'
CompanyName = 'SIBS'
Copyright = '(c) I.E.Sazonov. All rights reserved.'
Description = 'This module replaces the standard cmdlets (Import-Csv, ConvertFrom-Csv) with their optimized versions.'
FunctionsToExport = @()
AliasesToExport = @()
CmdletsToExport = 'Export-CSV', 'Import-CSV', 'ConvertTo-Csv', 'ConvertFrom-CSV'
VariablesToExport = '*'
PrivateData = @{

    PSData = @{

        # Tags applied to this module. These help with module discovery in online galleries.
        Tags = @('CSV', 'Sibs', 'Import-Csv', 'ConvertFrom-Csv')

        # A URL to the license for this module.
        LicenseUri = 'https://github.com/iSazonov/SibsCSV/LICENSE'

        # A URL to the main website for this project.
        ProjectUri = 'https://github.com/iSazonov/SibsCSV/'

        # A URL to an icon representing this module.
        # IconUri = ''

        # ReleaseNotes of this module
        # ReleaseNotes = ''

        # Prerelease string of this module
        # Prerelease = ''

        # Flag to indicate whether the module requires explicit user acceptance for install/update/save
        # RequireLicenseAcceptance = $false

        # External dependent modules of this module
        # ExternalModuleDependencies = @()

    } # End of PSData hashtable

} # End of PrivateData hashtable

# HelpInfo URI of this module
# HelpInfoURI = ''

# Default prefix for commands exported from this module. Override the default prefix using Import-Module -Prefix.
DefaultCommandPrefix = 'SIBS'

}

