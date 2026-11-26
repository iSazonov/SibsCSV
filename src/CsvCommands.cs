// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management.Automation;
using System.Text;

namespace SIBS.PowerShell.CSV
{
    #region BaseCsvWritingCommand

    /// <summary>
    /// This class implements the base for exportcsv and converttocsv commands.
    /// </summary>
    public abstract class BaseCsvWritingCommand : PSCmdlet
    {
        #region Command Line Parameters

        /// <summary>
        /// Property that sets delimiter.
        /// </summary>
        [Parameter(Position = 1, ParameterSetName = "Delimiter")]
        [ValidateNotNull]
        public char Delimiter { get; set; }

        /// <summary>
        /// Culture switch for csv conversion
        /// </summary>
        [Parameter(ParameterSetName = "UseCulture")]
        public SwitchParameter UseCulture { get; set; }

        /// <summary>
        /// Abstract Property - Input Object which is written in Csv format.
        /// Derived as Different Attributes.In ConvertTo-CSV, This is a positional parameter. Export-CSV not a Positional behavior.
        /// </summary>
        public abstract PSObject? InputObject { get; set; }

        /// <summary>
        /// IncludeTypeInformation : The #TYPE line should be generated. Default is false.
        /// </summary>
        [Parameter]
        [Alias("ITI")]
        public SwitchParameter IncludeTypeInformation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to suppress the #TYPE line.
        /// This parameter is obsolete and has no effect. It is retained for backward compatibility only.
        /// </summary>
        [Parameter(DontShow = true)]
        [Alias("NTI")]
        [Obsolete("This parameter is obsolete and has no effect. The default behavior is to not include type information. Use -IncludeTypeInformation to include type information.")]
        public SwitchParameter NoTypeInformation { get; set; } = true;

        /// <summary>
        /// Gets or sets list of fields to quote in output.
        /// </summary>
        [Parameter]
        [Alias("QF")]
        public string[]? QuoteFields { get; set; }

        /// <summary>
        /// Gets or sets option to use or suppress quotes in output.
        /// </summary>
        [Parameter]
        [Alias("UQ")]
        public QuoteKind UseQuotes { get; set; } = QuoteKind.Always;

        /// <summary>
        /// Gets or sets property that writes csv file with no headers.
        /// </summary>
        [Parameter]
        public SwitchParameter NoHeader { get; set; }

        #endregion Command Line Parameters

        /// <summary>
        /// Kind of output quoting.
        /// </summary>
        public enum QuoteKind
        {
            /// <summary>
            /// Never quote output.
            /// </summary>
            Never,

            /// <summary>
            /// Always quote output.
            /// </summary>
            Always,

            /// <summary>
            /// Quote output as needed (a field contains used delimiter).
            /// </summary>
            AsNeeded
        }

        /// <summary>
        /// BeginProcessing override.
        /// </summary>
        protected override void BeginProcessing()
        {
            if (MyInvocation.BoundParameters.ContainsKey(nameof(QuoteFields)) && MyInvocation.BoundParameters.ContainsKey(nameof(UseQuotes)))
            {
                InvalidOperationException exception = new(CsvCommandStrings.CannotSpecifyQuoteFieldsAndUseQuotes);
                ErrorRecord errorRecord = new(exception, "CannotSpecifyQuoteFieldsAndUseQuotes", ErrorCategory.InvalidData, null);
                ThrowTerminatingError(errorRecord);
            }

            Delimiter = ImportExportCSVHelper.SetDelimiter(this, ParameterSetName, Delimiter, UseCulture);
        }
    }
    #endregion

    #region Export-CSV Command

    /// <summary>
    /// Implementation for the Export-Csv command.
    /// </summary>
    [Cmdlet(VerbsData.Export, "Csv", SupportsShouldProcess = true, DefaultParameterSetName = "Delimiter", HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2096608")]
    public sealed class ExportCsvCommand : BaseCsvWritingCommand, IDisposable
    {
        #region Command Line Parameters

        // If a Passthru parameter is added, the ShouldProcess
        // implementation will need to be changed.

        /// <summary>
        /// Input Object for CSV Writing.
        /// </summary>
        [Parameter(ValueFromPipeline = true, Mandatory = true, ValueFromPipelineByPropertyName = true)]
        public override PSObject? InputObject { get; set; }

        /// <summary>
        /// Mandatory file name to write to.
        /// </summary>
        [Parameter(Position = 0)]
        [ValidateNotNullOrEmpty]
        public string? Path
        {
            get => _path;

            set
            {
                _path = value;
                _specifiedPath = true;
            }
        }

        private string? _path;
        private bool _specifiedPath = false;

        /// <summary>
        /// The literal path of the mandatory file name to write to.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [Alias("PSPath", "LP")]
        public string? LiteralPath
        {
            get => _path;

            set
            {
                _path = value;
                _isLiteralPath = true;
            }
        }

        private bool _isLiteralPath = false;

        /// <summary>
        /// Gets or sets property that sets force parameter.
        /// </summary>
        [Parameter]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Gets or sets property that prevents file overwrite.
        /// </summary>
        [Parameter]
        [Alias("NoOverwrite")]
        public SwitchParameter NoClobber { get; set; }

        /// <summary>
        /// Gets or sets encoding optional flag.
        /// </summary>
        [Parameter]
        [ArgumentToEncodingTransformation]
        [ArgumentEncodingCompletions]
        [ValidateNotNullOrEmpty]
        public Encoding Encoding
        {
            get => _encoding;

            set
            {
                EncodingConversion.WarnIfObsolete(this, value);
                _encoding = value;
            }
        }

        private Encoding _encoding = Encoding.Default;

        /// <summary>
        /// Gets or sets property that sets append parameter.
        /// </summary>
        [Parameter]
        public SwitchParameter Append { get; set; }

        // true if Append=true AND the file written was not empty (or nonexistent) when the cmdlet was invoked
        private bool _isActuallyAppending;

        #endregion

        #region Overrides

        private bool _shouldProcess;
        private List<string>? _propertyNames;
        private List<string>? _preexistingPropertyNames;
        private ExportCsvHelper _helper = null!;

        /// <summary>
        /// BeginProcessing override.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate that they don't provide both Path and LiteralPath, but have provided at least one.
            if (!(_specifiedPath ^ _isLiteralPath))
            {
                InvalidOperationException exception = new(CsvCommandStrings.CannotSpecifyPathAndLiteralPath);
                ErrorRecord errorRecord = new(exception, "CannotSpecifyPathAndLiteralPath", ErrorCategory.InvalidData, null);
                ThrowTerminatingError(errorRecord);
            }

            // Validate that Append and NoHeader are not specified together.
            if (Append && NoHeader)
            {
                InvalidOperationException exception = new(CsvCommandStrings.CannotSpecifyAppendAndNoHeader);
                ErrorRecord errorRecord = new(exception, "CannotSpecifyBothAppendAndNoHeader", ErrorCategory.InvalidData, null);
                ThrowTerminatingError(errorRecord);
            }

            _shouldProcess = ShouldProcess(Path);
            if (!_shouldProcess)
            {
                return;
            }

            CreateFileStream();

            _helper = new ExportCsvHelper(_sw, Delimiter, UseQuotes, QuoteFields);
        }

        /// <summary>
        /// Convert the current input object to Csv and write to file/WriteObject.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (InputObject is null || _sw is null)
            {
                return;
            }

            if (!_shouldProcess)
            {
                return;
            }

            // Process first object
            if (_propertyNames is null)
            {
                // figure out the column names (and lock-in their order)
                _propertyNames = ExportCsvHelper.BuildPropertyNames(InputObject, _propertyNames);
                if (_isActuallyAppending && _preexistingPropertyNames is not null)
                {
                    ReconcilePreexistingPropertyNames(_propertyNames);
                }

                // write headers (row1: typename + row2: column names)
                if (!_isActuallyAppending && !NoHeader.IsPresent)
                {
                    if (IncludeTypeInformation)
                    {
                         _sw!.WriteLine(ExportCsvHelper.GetTypeString(InputObject));
                    }

                    _helper.ConvertPropertyNamesCSV(_propertyNames);
                }
            }

            _helper.ConvertPSObjectToCSV(InputObject, _propertyNames);
        }

        #endregion Overrides

        #region file

        /// <summary>
        /// Stream writer used to write to file.
        /// </summary>
        private StreamWriter? _sw;

        /// <summary>
        /// Handle to file whose read-only attribute should be reset when we are done.
        /// </summary>
        private FileInfo? _readOnlyFileInfo;

        [MemberNotNull(nameof(_sw))]
        private void CreateFileStream()
        {
            if (_path is null)
            {
                throw new InvalidOperationException(CsvCommandStrings.FileNameIsAMandatoryParameter);
            }

            string resolvedFilePath = PathUtils.ResolveFilePath(_path, this, _isLiteralPath);

            bool isCsvFileEmpty = true;

            if (Append && File.Exists(resolvedFilePath))
            {
                using StreamReader streamReader = PathUtils.OpenStreamReader(this, _path, Encoding, _isLiteralPath);
                isCsvFileEmpty = streamReader.Peek() == -1;
            }

            // If the csv file is empty then even append is treated as regular export (i.e., both header & values are added to the CSV file).
            _isActuallyAppending = Append && File.Exists(resolvedFilePath) && !isCsvFileEmpty;

            if (_isActuallyAppending)
            {
                Encoding encodingObject;

                using (StreamReader streamReader = PathUtils.OpenStreamReader(this, _path, Encoding, _isLiteralPath))
                {
                    ImportCsvHelper readingHelper = new(this, Delimiter, header: null);
                    readingHelper.ReadHeader(streamReader);
                    _preexistingPropertyNames = readingHelper.Header;

                    encodingObject = streamReader.CurrentEncoding;
                }

                PathUtils.MasterStreamOpen(
                    this,
                    _path,
                    encodingObject,
                    defaultEncoding: false,
                    Append,
                    Force,
                    NoClobber,
                    out _,
                    out _sw,
                    out _readOnlyFileInfo,
                    _isLiteralPath);
            }
            else
            {
                PathUtils.MasterStreamOpen(
                    this,
                    _path,
                    Encoding,
                    defaultEncoding: false,
                    Append,
                    Force,
                    NoClobber,
                    out _,
                    out _sw,
                    out _readOnlyFileInfo,
                    _isLiteralPath);
            }
        }

        private void ReconcilePreexistingPropertyNames(List<string> propertyNames)
        {
            if (!_isActuallyAppending)
            {
                throw new InvalidOperationException(CsvCommandStrings.ReconcilePreexistingPropertyNamesMethodShouldOnlyGetCalledWhenAppending);
            }

            if (_preexistingPropertyNames is null)
            {
                throw new InvalidOperationException(CsvCommandStrings.ReconcilePreexistingPropertyNamesMethodShouldOnlyGetCalledWhenPreexistingPropertyNamesHaveBeenReadSuccessfully);
            }

            HashSet<string> appendedPropertyNames = new(propertyNames, StringComparer.OrdinalIgnoreCase);

            foreach (string preexistingPropertyName in _preexistingPropertyNames)
            {
                if (!appendedPropertyNames.Contains(preexistingPropertyName))
                {
                    if (!Force)
                    {
                        string errorMessage = string.Format(
                            CultureInfo.InvariantCulture, // property names and file names are culture invariant
                            CsvCommandStrings.CannotAppendCsvWithMismatchedPropertyNames,
                            preexistingPropertyName,
                            Path);
                        InvalidOperationException exception = new(errorMessage);
                        ErrorRecord errorRecord = new(exception, "CannotAppendCsvWithMismatchedPropertyNames", ErrorCategory.InvalidData, preexistingPropertyName);
                        ThrowTerminatingError(errorRecord);
                    }
                }
            }

            _propertyNames = _preexistingPropertyNames;
            _preexistingPropertyNames = null;
        }

        #endregion file

        #region IDisposable Members

        /// <summary>
        /// Set to true when object is disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Public dispose method.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_sw is not null)
                {
                    _sw.Dispose();
                    // It is bug to set _sw to null since we can get NRE instead of ObjectDisposedException after pipeline interruption.
                    //_sw = null;

                    // reset the read-only attribute
                    if (_readOnlyFileInfo is not null)
                    {
                        _readOnlyFileInfo.Attributes |= FileAttributes.ReadOnly;
                    }
                }
            }

            _disposed = true;
        }

        #endregion IDisposable Members
    }

    #endregion Export-CSV Command

    /// <summary>
    /// This class implements the base for Import-Csv and ConvertFrom-Csv commands.
    /// </summary>
    public abstract class BaseCsvReadingCommand : PSCmdlet
    {
        /// <summary>
        /// Gets or sets property that sets delimiter.
        /// </summary>
        [Parameter(Position = 1, ParameterSetName = "DelimiterPath")]
        [Parameter(Position = 1, ParameterSetName = "DelimiterLiteralPath")]
        [ValidateNotNull]
        public char Delimiter { get; set; }

        /// <summary>
        /// Gets or sets property that sets UseCulture parameter.
        /// </summary>
        [Parameter(ParameterSetName = "CulturePath", Mandatory = true)]
        [Parameter(ParameterSetName = "CultureLiteralPath", Mandatory = true)]
        [ValidateNotNull]
        public SwitchParameter UseCulture { get; set; }

        /// <summary>
        /// Gets or sets header property to customize the names.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string[]? Header { get; set; }

        private protected List<string>? _header;
        private protected ImportCsvHelper _csvParser;

        /// <summary>
        /// BeginProcessing override.
        /// </summary>
        protected override void BeginProcessing()
        {
            Delimiter = ImportExportCSVHelper.SetDelimiter(this, ParameterSetName, Delimiter, UseCulture);

            _header = Header is null ? null : new List<string>(Header);

            _csvParser = new(this, Delimiter, Header is null ? null : new List<string>(Header));

        }


    }

    #region Import-CSV Command

    /// <summary>
    /// Implements Import-Csv command.
    /// </summary>
    [Cmdlet(VerbsData.Import, "Csv", DefaultParameterSetName = "DelimiterPath", HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2097020")]
    public sealed class ImportCsvCommand : BaseCsvReadingCommand
    {
        #region Command Line Parameters

        /// <summary>
        /// Gets or sets mandatory file name to read from.
        /// </summary>
        [Parameter(Position = 0, ParameterSetName = "DelimiterPath", Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
        [Parameter(Position = 0, ParameterSetName = "CulturePath", Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        public string[]? Path
        {
            get => _paths;

            set
            {
                _paths = value;
                _specifiedPath = true;
            }
        }

        private string[]? _paths;
        private bool _specifiedPath = false;

        /// <summary>
        /// Gets or sets the literal path of the mandatory file name to read from.
        /// </summary>
        [Parameter(ParameterSetName = "DelimiterLiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
        [Parameter(ParameterSetName = "CultureLiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [Alias("PSPath", "LP")]
        public string[]? LiteralPath
        {
            get => _paths;

            set
            {
                _paths = value;
                _isLiteralPath = true;
            }
        }

        private bool _isLiteralPath = false;

        /// <summary>
        /// Gets or sets encoding optional flag.
        /// </summary>
        [Parameter]
        [ArgumentToEncodingTransformation]
        [ArgumentEncodingCompletions]
        [ValidateNotNullOrEmpty]
        public Encoding Encoding
        {
            get => _encoding;

            set
            {
                EncodingConversion.WarnIfObsolete(this, value);
                _encoding = value;
            }
        }

        private Encoding _encoding = Encoding.Default;

        #endregion Command Line Parameters

        #region Override Methods
        /// <summary>
        /// ProcessRecord overload.
        /// </summary>
        protected override void ProcessRecord()
        {
            // Validate that they don't provide both Path and LiteralPath, but have provided at least one.
            if (!(_specifiedPath ^ _isLiteralPath))
            {
                InvalidOperationException exception = new(CsvCommandStrings.CannotSpecifyPathAndLiteralPath);
                ErrorRecord errorRecord = new(exception, "CannotSpecifyPathAndLiteralPath", ErrorCategory.InvalidData, null);
                ThrowTerminatingError(errorRecord);
            }

            if (_paths is not null)
            {
                foreach (string path in _paths)
                {
                    using StreamReader streamReader = PathUtils.OpenStreamReader(this, path, Encoding, _isLiteralPath);

                    try
                    {
                        _csvParser.Import(streamReader);
                    }
                    catch (ExtendedTypeSystemException exception)
                    {
                        ErrorRecord errorRecord = new(exception, "AlreadyPresentPSMemberInfoInternalCollectionAdd", ErrorCategory.NotSpecified, null);
                        ThrowTerminatingError(errorRecord);
                    }
                }
            }
        }
    }
    #endregion Override Methods

    #endregion Import-CSV Command

    #region ConvertTo-CSV Command

    /// <summary>
    /// Implements ConvertTo-Csv command.
    /// </summary>
    [Cmdlet(VerbsData.ConvertTo, "Csv", DefaultParameterSetName = "Delimiter",
        HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2096832", RemotingCapability = RemotingCapability.None)]
    [OutputType(typeof(string))]
    public sealed class ConvertToCsvCommand : BaseCsvWritingCommand
    {
        #region Overrides

        /// <summary>
        /// Overrides Base InputObject.
        /// </summary>
        [Parameter(ValueFromPipeline = true, Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 0)]
        public override PSObject? InputObject { get; set; }

        private List<string>? _propertyNames;
        private ExportCsvHelper _helper = null!;
        private StringWriter _sw = new StringWriter(new StringBuilder(ImportCsvHelper.LineLengthGuesstimate));

        /// <summary>
        /// BeginProcessing override.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            _helper = new ExportCsvHelper(_sw, Delimiter, UseQuotes, QuoteFields);
        }

        /// <summary>
        /// Convert the current input object to Csv and write to stream/WriteObject.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (InputObject is null)
            {
                return;
            }

            // Process first object
            if (_propertyNames is null)
            {
                _propertyNames = ExportCsvHelper.BuildPropertyNames(InputObject, _propertyNames);

                if (!NoHeader.IsPresent)
                {
                    if (IncludeTypeInformation)
                    {
                        WriteObject(ExportCsvHelper.GetTypeString(InputObject));
                    }

                    // Write property information
                    _helper.ConvertPropertyNamesCSV(_propertyNames);
                }
            }

            _helper.ConvertPSObjectToCSV(InputObject, _propertyNames);
            string csv = _sw.GetStringBuilder().ToString();

            // Write to the output stream
            if (csv != string.Empty)
            {
                WriteObject(csv);
            }

            _sw.GetStringBuilder().Clear();
        }

        #endregion Overrides
    }

    #endregion ConvertTo-CSV Command

    #region ConvertFrom-CSV Command

    /// <summary>
    /// Implements ConvertFrom-Csv command.
    /// </summary>
    [Cmdlet(VerbsData.ConvertFrom, "Csv", DefaultParameterSetName = "Delimiter",
        HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2096830", RemotingCapability = RemotingCapability.None)]
    public sealed class ConvertFromCsvCommand : BaseCsvReadingCommand
    {
        /// <summary>
        /// Gets or sets input object which is written in Csv format.
        /// </summary>
        [Parameter(ValueFromPipeline = true, Mandatory = true, ValueFromPipelineByPropertyName = true, Position = 0)]
        [ValidateNotNull]
        [ValidateNotNullOrEmpty]
        public PSObject[] InputObject { get; set; } = null!;

        /// <summary>
        /// Convert the current input object to Csv and write to stream/WriteObject.
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (PSObject inputObject in InputObject)
            {
                using (TextReader stringReader = new StringReader(inputObject.ToString()))
                {
                    try
                    {
                        _csvParser.Import(stringReader);
                    }
                    catch (ExtendedTypeSystemException exception)
                    {
                        ErrorRecord errorRecord = new(exception, "AlreadyPresentPSMemberInfoInternalCollectionAdd", ErrorCategory.NotSpecified, null);
                        ThrowTerminatingError(errorRecord);
                    }
                }
            }
        }
    }

    #endregion ConvertFrom-CSV Command

    #region CSV conversion

    #region ExportHelperConversion

    /// <summary>
    /// Helper class for Export-Csv and ConvertTo-Csv.
    /// </summary>
    internal sealed class ExportCsvHelper
    {
        private readonly TextWriter _writer;
        private readonly char _delimiter;
        private readonly BaseCsvWritingCommand.QuoteKind _quoteKind;
        private readonly HashSet<string>? _quoteFields;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportCsvHelper"/> class.
        /// </summary>
        /// <param name="delimiter">Delimiter char.</param>
        /// <param name="quoteKind">Kind of quoting.</param>
        /// <param name="quoteFields">List of fields to quote.</param>
        internal ExportCsvHelper(TextWriter writer, char delimiter, BaseCsvWritingCommand.QuoteKind quoteKind, string[]? quoteFields)
        {
            _writer = writer;
            _delimiter = delimiter;
            _quoteKind = quoteKind;
            _quoteFields = quoteFields is null ? null : new HashSet<string>(quoteFields, StringComparer.OrdinalIgnoreCase);
        }

        // Name of properties to be written in CSV format

        /// <summary>
        /// Get the name of properties from source PSObject and add them to _propertyNames.
        /// </summary>
        internal static List<string> BuildPropertyNames(PSObject source, List<string>? propertyNames)
        {
            if (propertyNames is not null)
            {
                throw new InvalidOperationException(CsvCommandStrings.BuildPropertyNamesMethodShouldBeCalledOnlyOncePerCmdletInstance);
            }

            propertyNames = new List<string>(ImportCsvHelper.ValueCountGuesstimate);
            if (source.BaseObject is IDictionary dictionary)
            {
                foreach (var key in dictionary.Keys)
                {
                    propertyNames.Add(LanguagePrimitives.ConvertTo<string>(key));
                }

                // Add additional extended members added to the dictionary object, if any
                if (source.Members["psextended"] is PSMemberSet extendedSet)
                {
                    var extendedProperties = extendedSet.Properties;

                    foreach (PSPropertyInfo prop in extendedProperties)
                    {
                        propertyNames.Add(prop.Name);
                    }
                }
            }
            else
            {
                // serialize only Extended and Adapted properties.
                /*
                PSMemberInfoCollection<PSPropertyInfo> srcPropertiesToSearch =
                    new PSMemberInfoIntegratingCollection<PSPropertyInfo>(
                        source,
                        PSObject.GetPropertyCollection(PSMemberViewTypes.Extended | PSMemberViewTypes.Adapted));

                foreach (PSPropertyInfo prop in srcPropertiesToSearch)
                {
                    propertyNames.Add(prop.Name);
                }
                */
                if (source.Members["psextended"] is PSMemberSet extendedSet)
                {
                    var extendedProperties = extendedSet.Properties;

                    foreach (PSPropertyInfo prop in extendedProperties)
                    {
                        propertyNames.Add(prop.Name);
                    }
                }

                if (source.Members["psadapted"] is PSMemberSet adaptedSet)
                {
                    var extendedProperties = adaptedSet.Properties;

                    foreach (PSPropertyInfo prop in extendedProperties)
                    {
                        propertyNames.Add(prop.Name);
                    }
                }
            }

            return propertyNames;
        }

        /// <summary>
        /// Converts PropertyNames in to a CSV string.
        /// </summary>
        /// <returns>Converted string.</returns>
        internal void ConvertPropertyNamesCSV(List<string> propertyNames)
        {
            ArgumentNullException.ThrowIfNull(propertyNames);

            bool first = true;

            foreach (string propertyName in propertyNames)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    _writer.Write(_delimiter);
                }

                if (_quoteFields is not null)
                {
                    if (_quoteFields.TryGetValue(propertyName, out _))
                    {
                        AppendStringWithEscapeAlways(_writer, propertyName);
                    }
                    else
                    {
                        _writer.Write(propertyName);
                    }
                }
                else
                {
                    switch (_quoteKind)
                    {
                        case BaseCsvWritingCommand.QuoteKind.Always:
                            AppendStringWithEscapeAlways(_writer, propertyName);
                            break;
                        case BaseCsvWritingCommand.QuoteKind.AsNeeded:

                            if (propertyName.AsSpan().IndexOfAny(_delimiter, '\n', '"') != -1)
                            {
                                AppendStringWithEscapeAlways(_writer, propertyName);
                            }
                            else
                            {
                                _writer.Write(propertyName);
                            }

                            break;
                        case BaseCsvWritingCommand.QuoteKind.Never:
                            _writer.Write(propertyName);
                            break;
                    }
                }
            }

            return;
        }

        /// <summary>
        /// Convert PSObject to CSV string.
        /// </summary>
        /// <param name="mshObject">PSObject to convert.</param>
        /// <param name="propertyNames">Property names.</param>
        /// <returns></returns>
        internal void ConvertPSObjectToCSV(PSObject mshObject, List<string> propertyNames)
        {
            ArgumentNullException.ThrowIfNull(propertyNames);

            bool first = true;
            var dictionary = mshObject.BaseObject as IDictionary;

            foreach (string propertyName in propertyNames)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    _writer.Write(_delimiter);
                }

                string? value = null;
                if (dictionary is not null && dictionary.Contains(propertyName))
                {
                    value = dictionary[propertyName]?.ToString();
                }
                else if (mshObject.Properties[propertyName] is PSPropertyInfo property)
                {
                    value = GetToStringValueForProperty(property);
                }

                // If value is null, assume property is not present and skip it.
                if (value is not null)
                {
                    if (_quoteFields is not null)
                    {
                        if (_quoteFields.TryGetValue(propertyName, out _))
                        {
                            AppendStringWithEscapeAlways(_writer, value);
                        }
                        else
                        {
                            _writer.Write(value);
                        }
                    }
                    else
                    {
                        switch (_quoteKind)
                        {
                            case BaseCsvWritingCommand.QuoteKind.Always:
                                AppendStringWithEscapeAlways(_writer, value);
                                break;
                            case BaseCsvWritingCommand.QuoteKind.AsNeeded:
                                if (value is not null && value.AsSpan().IndexOfAny(_delimiter, '\n', '"') != -1)
                                {
                                    AppendStringWithEscapeAlways(_writer, value);
                                }
                                else
                                {
                                    _writer.Write(value);
                                }

                                break;
                            case BaseCsvWritingCommand.QuoteKind.Never:
                                _writer.Write(value);
                                break;
                            default:
                                //Diagnostics.Assert(false, "BaseCsvWritingCommand.QuoteKind has new item.");
                                break;
                        }
                    }
                }
            }

            return;
        }

        /// <summary>
        /// Get value from property object.
        /// </summary>
        /// <param name="property"> Property to convert.</param>
        /// <returns>ToString() value.</returns>
        internal static string? GetToStringValueForProperty(PSPropertyInfo property)
        {
            ArgumentNullException.ThrowIfNull(property);

            string? value = null;
            try
            {
                object temp = property.Value;
                if (temp is not null)
                {
                    value = temp.ToString();
                }
            }
            catch (Exception)
            {
                // If we cannot read some value, treat it as null.
            }

            return value;
        }

        /// <summary>
        /// Prepares string for writing type information.
        /// </summary>
        /// <param name="source">PSObject whose type to determine.</param>
        /// <returns>String with type information.</returns>
        internal static string GetTypeString(PSObject source)
        {
            string? type = null;

            // get type of source
            Collection<string> tnh = source.TypeNames;
            if (tnh is null || tnh.Count == 0)
            {
                type = "#TYPE";
            }
            else
            {
                if (tnh[0] is null)
                {
                    throw new InvalidOperationException(CsvCommandStrings.TypeHierarchyShouldNotHaveNullValues);
                }

                string temp = tnh[0];

                // If type starts with CSV: remove it. This would happen when you export
                // an imported object. import-csv adds CSV. prefix to the type.
                if (temp.StartsWith(ImportExportCSVHelper.CSVTypePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    temp = temp.Substring(4);
                }

                type = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#TYPE {temp}");
            }

            return type;
        }

        /// <summary>
        /// Escapes the " in string if necessary.
        /// Encloses the string in double quotes if necessary.
        /// </summary>
        internal static void AppendStringWithEscapeAlways(TextWriter dest, string source)
        {
            if (source is null)
            {
                return;
            }

            // Adding Double quote to all strings
            dest.Write('"');
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                // Double quote in the string is escaped with double quote
                if (c == '"')
                {
                    dest.Write('"');
                }

                dest.Write(c);
            }

            dest.Write('"');
        }
    }

    #endregion ExportHelperConversion

    #region ImportHelperConversion

    /// <summary>
    /// Helper class to import single CSV file.
    /// </summary>
    internal struct ImportCsvHelper
    {
        #region constructor

        /// <summary>
        /// Reference to cmdlet which is using this helper class.
        /// </summary>
        private readonly PSCmdlet _cmdlet;

        /// <summary>
        /// CSV delimiter (default is the "comma" / "," character).
        /// </summary>
        private char _delimiter;

        /// <summary>
        /// Use "UnspecifiedName" when the name is null or empty.
        /// </summary>
        private const string UnspecifiedName = "H";

        /// <summary>
        /// Avoid writing out duplicate warning messages when there are one or more unspecified names.
        /// </summary>
        private bool _alreadyWarnedUnspecifiedName = false;

        /// <summary>
        /// Gets reference to header values.
        /// </summary>
        internal List<string>? Header { get; private set; }

        /// <summary>
        /// Gets ETS type name from the first line / comment in the CSV.
        /// </summary>
        private string? _typeName;

        /// <summary>
        /// Reader of the csv content.
        /// </summary>
        private TextReader _textReader;

        private readonly StringBuilder _currentValue;


        // Initial sizes of the value list and the line StringBuilder.
        // Set to reasonable initial sizes based on real log sizes.
        // They may grow beyond these, but this will prevent a few reallocations.
        internal const int ValueCountGuesstimate = 64;
        internal const int LineLengthGuesstimate = 1024;
        private const int ValueLengthGuesstimate = 128;
        private readonly List<string> _values;
        private readonly StringBuilder _builder;

        //internal ImportCsvHelper(PSCmdlet cmdlet, char delimiter, List<string>? header, string? typeName, TextReader streamReader)
        internal ImportCsvHelper(PSCmdlet cmdlet, char delimiter, List<string>? header)
        {
            ArgumentNullException.ThrowIfNull(cmdlet);

            _cmdlet = cmdlet;
            _delimiter = delimiter;
            Header = header;
            _textReader = null!; // Public methods (Import() and ReadHeader()) have the non-null parameter.
            _values = new(ValueCountGuesstimate);
            _builder = new(LineLengthGuesstimate);
            _currentValue = new(ValueLengthGuesstimate);

        }

        #endregion constructor

        #region reading helpers

        /// <summary>
        /// This is set to true when end of file is reached.
        /// </summary>
        private readonly bool EOF => _textReader.Peek() == -1;

        private char ReadChar()
        {
            if (EOF)
            {
                throw new InvalidOperationException(CsvCommandStrings.EOFIsReached);
            }

            int i = _textReader.Read();
            return (char)i;
        }

        /// <summary>
        /// Peeks the next character in the stream and returns true if it is same as passed in character.
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        private readonly bool PeekNextChar(char c)
        {
            int i = _textReader.Peek();
            if (i == -1)
            {
                return false;
            }

            return c == (char)i;
        }

        /// <summary>
        /// Reads a line from file. This consumes the end of line.
        /// Only use it when end of line chars are not important.
        /// </summary>
        /// <returns>Line from file.</returns>
        private string? ReadLine() => _textReader.ReadLine();

        #endregion reading helpers

        internal void ReadHeader(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            _textReader = reader;

            // Read #Type record if available
            if ((_typeName is null) && (!EOF))
            {
                _typeName = ReadTypeInformation();
            }

            //List<string>? _values = new(ValueCountGuesstimate);
            //StringBuilder? _builder = new(LineLengthGuesstimate);
            while ((Header is null) && (!EOF))
            {
                ParseNextRecord();

                // Trim all trailing blankspaces and delimiters ( single/multiple ).
                // If there is only one element in the row and if its a blankspace we don't trim it.
                // A trailing delimiter is represented as a blankspace while being added to result collection
                // which is getting trimmed along with blankspaces supplied through the CSV in the below loop.
                while (_values.Count > 1 && _values[_values.Count - 1].Equals(string.Empty))
                {
                    _values.RemoveAt(_values.Count - 1);
                }

                // File starts with '#' and contains '#Fields:' is W3C Extended Log File Format
                if (_values.Count != 0 && _values[0].StartsWith("#Fields: "))
                {
                    _values[0] = _values[0].Substring(9);
                    Header = _values;
                }
                else if (_values.Count != 0 && _values[0].StartsWith('#'))
                {
                    // Skip all lines starting with '#'
                }
                else
                {
                    // This is not W3C Extended Log File Format
                    // By default first line is Header
                    Header = _values;
                }
            }

            if (Header is not null && Header.Count > 0)
            {
                ValidatePropertyNames(Header);
            }
        }

        internal void Import(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            _textReader = reader;

            ReadHeader(reader);
            var prevalidated = false;
            //List<string>? _values = new(ValueCountGuesstimate);
            //StringBuilder _builder = new(LineLengthGuesstimate);
            while (true)
            {
                ParseNextRecord();
                if (_values.Count == 0)
                    break;

                if (_values.Count == 1 && string.IsNullOrEmpty(_values[0]))
                {
                    // skip the blank lines
                    continue;
                }

                PSObject result = BuildMshObject(_typeName, Header!, _values, _delimiter, prevalidated);
                prevalidated = true;
                _cmdlet.WriteObject(result);
            }
        }

        /// <summary>
        /// Validate the names of properties.
        /// </summary>
        /// <param name="names"></param>
        private static void ValidatePropertyNames(List<string> names)
        {
            if (names is not null)
            {
                if (names.Count == 0)
                {
                    // If there are no names, it is an error
                }
                else
                {
                    HashSet<string> headers = new(StringComparer.OrdinalIgnoreCase);
                    foreach (string currentHeader in names)
                    {
                        if (!string.IsNullOrEmpty(currentHeader))
                        {
                            if (!headers.Add(currentHeader))
                            {
                                // throw a terminating error as there are duplicate headers in the input.
                                string memberAlreadyPresentMsg =
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        "ExtendedTypeSystem.MemberAlreadyPresent",
                                        currentHeader);

                                ExtendedTypeSystemException exception = new(memberAlreadyPresentMsg);
                                throw exception;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read the type information, if present.
        /// </summary>
        /// <returns>Type string if present else null.</returns>
        private string? ReadTypeInformation()
        {
            string? type = null;
            if (PeekNextChar('#'))
            {
                string? temp = ReadLine();
                if (temp is not null && temp.StartsWith("#Type", StringComparison.OrdinalIgnoreCase))
                {
                    type = temp.Substring(5);
                    type = type.Trim();
                    if (type.Length == 0)
                    {
                        type = null;
                    }
                }
            }

            return type;
        }

        /// <summary>
        /// Reads the next record from the file and returns parsed collection of string.
        /// </summary>
        /// <returns>
        /// Parsed collection of strings.
        /// </returns>
        //private void ParseNextRecord(List<string> _values, StringBuilder _builder)
        private void ParseNextRecord()
        {
            _values.Clear();

            // current string
            _builder.Clear();

            bool seenBeginQuote = false;

            while (!EOF)
            {
                // Read the next character
                char ch = ReadChar();

                if (ch == _delimiter)
                {
                    if (seenBeginQuote)
                    {
                        // Delimiter inside double quotes is part of string.
                        // Ex:
                        // "foo, bar"
                        // is parsed as
                        // ->foo, bar<-
                        _builder.Append(ch);
                    }
                    else
                    {
                        // Delimiter outside quotes is end of current word.
                        _values.Add(_builder.ToString());
                        _builder.Remove(0, _builder.Length);
                    }
                }
                else if (ch == '"')
                {
                    if (seenBeginQuote)
                    {
                        if (PeekNextChar('"'))
                        {
                            // "" inside double quote are single quote
                            // ex: "foo""bar"
                            // is read as
                            // ->foo"bar<-

                            // PeekNextChar only peeks. Read the next char.
                            ReadChar();
                            _builder.Append('"');
                        }
                        else
                        {
                            // We have seen a matching end quote.
                            seenBeginQuote = false;

                            // Read
                            // everything till we hit next delimiter.
                            // In correct CSV,1) end quote is followed by delimiter
                            // 2)end quote is followed some whitespaces and
                            // then delimiter.
                            // We eat the whitespaces seen after the ending quote.
                            // However if there are other characters, we add all of them
                            // to string.
                            // Ex: ->"foo bar"<- is read as ->foo bar<-
                            // ->"foo bar"  <- is read as ->foo bar<-
                            // ->"foo bar" ab <- is read as ->"foo bar" ab <-
                            bool endOfRecord = false;
                            ReadTillNextDelimiter(_builder, ref endOfRecord, true);
                            _values.Add(_builder.ToString());
                            _builder.Remove(0, _builder.Length);
                            if (endOfRecord)
                            {
                                break;
                            }
                        }
                    }
                    else if (_builder.Length == 0)
                    {
                        // We are at the beginning of a new word.
                        // This quote is the first quote.
                        seenBeginQuote = true;
                    }
                    else
                    {
                        // We are seeing a quote after the start of
                        // the word. This is error, however we will be
                        // lenient here and do what excel does:
                        // Ex: foo "ba,r"
                        // In above example word read is ->foo "ba<-
                        // Basically we read till next delimiter
                        bool endOfRecord = false;
                        _builder.Append(ch);
                        ReadTillNextDelimiter(_builder, ref endOfRecord, false);
                        _values.Add(_builder.ToString());
                        _builder.Remove(0, _builder.Length);
                        if (endOfRecord)
                        {
                            break;
                        }
                    }
                }
                else if (ch == ' ' || ch == '\t')
                {
                    if (seenBeginQuote)
                    {
                        // Spaces in side quote are valid
                        _builder.Append(ch);
                    }
                    else if (_builder.Length == 0)
                    {
                        // ignore leading spaces
                        continue;
                    }
                    else
                    {
                        // We are not in quote and we are not at the
                        // beginning of a word. We should not be seeing
                        // spaces here. This is an error condition, however
                        // we will be lenient here and do what excel does,
                        // that is read till next delimiter.
                        // Ex: ->foo <- is read as ->foo<-
                        // Ex: ->foo bar<- is read as ->foo bar<-
                        // Ex: ->foo bar <- is read as ->foo bar <-
                        // Ex: ->foo bar "er,ror"<- is read as ->foo bar "er<-
                        bool endOfRecord = false;
                        _builder.Append(ch);
                        ReadTillNextDelimiter(_builder, ref endOfRecord, true);
                        _values.Add(_builder.ToString());
                        _builder.Remove(0, _builder.Length);

                        if (endOfRecord)
                        {
                            break;
                        }
                    }
                }
                else if (IsNewLine(ch, out string newLine))
                {
                    if (seenBeginQuote)
                    {
                        // newline inside quote are valid
                        _builder.Append(newLine);
                    }
                    else
                    {
                        _values.Add(_builder.ToString());
                        _builder.Remove(0, _builder.Length);

                        // New line outside quote is end of word and end of record
                        break;
                    }
                }
                else
                {
                    _builder.Append(ch);
                }
            }

            if (_builder.Length != 0)
            {
                _values.Add(_builder.ToString());
            }
        }

        // If we detect a newline we return it as a string "\r", "\n" or "\r\n"
        private bool IsNewLine(char ch, out string newLine)
        {
            newLine = string.Empty;
            if (ch == '\r')
            {
                if (PeekNextChar('\n'))
                {
                    ReadChar();
                    newLine = "\r\n";
                }
                else
                {
                    newLine = "\r";
                }
            }
            else if (ch == '\n')
            {
                newLine = "\n";
            }

            return newLine != string.Empty;
        }

        /// <summary>
        /// This function reads the characters till next delimiter and adds them to current.
        /// </summary>
        /// <param name="current"></param>
        /// <param name="endOfRecord">
        /// This is true if end of record is reached
        /// when delimiter is hit. This would be true if delimiter is NewLine.
        /// </param>
        /// <param name="eatTrailingBlanks">
        /// If this is true, eat the trailing blanks. Note:if there are non
        /// whitespace characters present, then trailing blanks are not consumed.
        /// </param>
        private void ReadTillNextDelimiter(StringBuilder current, ref bool endOfRecord, bool eatTrailingBlanks)
        {
            // Did we see any non-whitespace character
            bool nonWhiteSpace = false;

            while (true)
            {
                if (EOF)
                {
                    endOfRecord = true;
                    break;
                }

                char ch = ReadChar();

                if (ch == _delimiter)
                {
                    break;
                }
                else if (IsNewLine(ch, out string newLine))
                {
                    endOfRecord = true;
                    break;
                }
                else
                {
                    _currentValue.Append(ch);
                    if (ch != ' ' && ch != '\t')
                    {
                        nonWhiteSpace = true;
                    }
                }
            }

            if (eatTrailingBlanks && !nonWhiteSpace)
            {
                string s = _currentValue.ToString();
                s = s.Trim();
                current.Append(s);
            }
            else
            {
                current.Append(_currentValue);
            }
        }

        private PSObject BuildMshObject(string? type, List<string> names, List<string> values, char delimiter, bool preValidated = false)
        {
            PSObject result = new(names.Count);
            char delimiterLocal = delimiter;
            int unspecifiedNameIndex = 1;
            for (int i = 0; i <= names.Count - 1; i++)
            {
                string name = names[i];
                string? value = null;

                // if name is null and delimiter is '"', use a default property name 'UnspecifiedName'
                if (name.Length == 0 && delimiterLocal == '"')
                {
                    name = UnspecifiedName + unspecifiedNameIndex;
                    unspecifiedNameIndex++;
                }

                // if name is null and delimiter is not '"', use a default property name 'UnspecifiedName'
                if (string.IsNullOrEmpty(name))
                {
                    name = UnspecifiedName + unspecifiedNameIndex;
                    unspecifiedNameIndex++;
                }

                // If no value was present in CSV file, we write null.
                if (i < values.Count)
                {
                    value = values[i];
                }

                result.Properties.Add(new PSNoteProperty(name, value), preValidated);
            }

            if (!_alreadyWarnedUnspecifiedName && unspecifiedNameIndex != 1)
            {
                _cmdlet.WriteWarning(CsvCommandStrings.UseDefaultNameForUnspecifiedHeader);
                _alreadyWarnedUnspecifiedName = true;
            }

            if (!string.IsNullOrEmpty(type))
            {
                result.TypeNames.Clear();
                result.TypeNames.Add(type);
                result.TypeNames.Add(ImportExportCSVHelper.CSVTypePrefix + type);
            }

            return result;
        }
    }

    #endregion ImportHelperConversion

    #region ExportImport Helper

    /// <summary>
    /// Helper class for CSV conversion.
    /// </summary>
    internal static class ImportExportCSVHelper
    {
        internal const char CSVDelimiter = ',';
        internal const string CSVTypePrefix = "CSV:";

        internal static char SetDelimiter(PSCmdlet cmdlet, string parameterSetName, char explicitDelimiter, bool useCulture)
        {
            char delimiter = explicitDelimiter;
            switch (parameterSetName)
            {
                case "Delimiter":
                case "DelimiterPath":
                case "DelimiterLiteralPath":

                    // if delimiter is not given, it should take , as value
                    if (explicitDelimiter == '\0')
                    {
                        delimiter = ImportExportCSVHelper.CSVDelimiter;
                    }

                    break;
                case "UseCulture":
                case "CulturePath":
                case "CultureLiteralPath":
                    if (useCulture)
                    {
                        // ListSeparator is apparently always a character even though the property returns a string, checked via:
                        // [CultureInfo]::GetCultures("AllCultures") | % { ([CultureInfo]($_.Name)).TextInfo.ListSeparator } | ? Length -ne 1
                        delimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator[0];
                    }

                    break;
                default:
                    {
                        delimiter = ImportExportCSVHelper.CSVDelimiter;
                    }

                    break;
            }

            return delimiter;
        }
    }

    #endregion ExportImport Helper

    #endregion CSV conversion
}
