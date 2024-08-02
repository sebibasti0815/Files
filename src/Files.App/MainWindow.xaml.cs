using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WinUIEx;
using HWND = Windows.Win32.Foundation.HWND;
using IO = System.IO;

namespace Files.App;

public sealed partial class MainWindow : WindowEx {
	private static MainWindow? _Instance;

	private MainWindow() {
		this.WindowHandle = this.GetWindowHandle();

		InitializeComponent();

		EnsureEarlyWindow();
	}

	public static MainWindow Instance => _Instance ??= new MainWindow();

	public IntPtr WindowHandle { get; }

	public Frame EnsureWindowIsInitialized() {
		// NOTE:
		//  Do not repeat app initialization when the Window already has content,
		//  just ensure that the window is active
		if (Instance.Content is not Frame rootFrame) {
			// Create a Frame to act as the navigation context and navigate to the first page
			rootFrame = new Frame {
				CacheSize = 1
			};
			rootFrame.NavigationFailed += OnNavigationFailed;

			// Place the frame in the current Window
			Instance.Content = rootFrame;
		}

		return rootFrame;
	}

	public async Task InitializeApplicationAsync(object activatedEventArgs) {
		Frame rootFrame = EnsureWindowIsInitialized();

		// Set system backdrop
		this.SystemBackdrop = new AppSystemBackdrop();

		switch (activatedEventArgs) {
			case ILaunchActivatedEventArgs launchArgs:
				if (launchArgs.Arguments is not null &&
				    (CommandLineParser.SplitArguments(launchArgs.Arguments, true)[0]
				                      .EndsWith("files.exe", StringComparison.OrdinalIgnoreCase) ||
				     CommandLineParser.SplitArguments(launchArgs.Arguments, true)[0]
				                      .EndsWith("files", StringComparison.OrdinalIgnoreCase))) {
					// WINUI3: When launching from commandline the argument is not ICommandLineActivatedEventArgs (#10370)
					ParsedCommands ppm = CommandLineParser.ParseUntrustedCommands(launchArgs.Arguments);
					if (ppm.IsEmpty())
						rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
					else
						await InitializeFromCmdLineArgsAsync(rootFrame, ppm);
				} else if (rootFrame.Content is null             ||
				           rootFrame.Content is SplashScreenPage ||
				           !MainPageViewModel.AppInstances.Any()) {
					// When the navigation stack isn't restored navigate to the first page,
					// configuring the new page by passing required information as a navigation parameter
					rootFrame.Navigate(typeof(MainPage), launchArgs.Arguments, new SuppressNavigationTransitionInfo());
				} else if (!(string.IsNullOrEmpty(launchArgs.Arguments) && MainPageViewModel.AppInstances.Count > 0)) {
					// Bring to foreground (#14730)
					Win32Helper.BringToForegroundEx(new HWND(this.WindowHandle));

					await NavigationHelpers.AddNewTabByPathAsync(typeof(ShellPanesPage), launchArgs.Arguments, true);
				} else {
					rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
				}

				break;

			case IProtocolActivatedEventArgs eventArgs:
				if (eventArgs.Uri.AbsoluteUri == "files-uwp:") {
					rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());

					if (MainPageViewModel.AppInstances.Count > 0) {
						// Bring to foreground (#14730)
						Win32Helper.BringToForegroundEx(new HWND(this.WindowHandle));
					}
				} else {
					string[] parsedArgs     = eventArgs.Uri.Query.TrimStart('?').Split('=');
					string?  unescapedValue = Uri.UnescapeDataString(parsedArgs[1]);
					StorageFolder? folder =
						(StorageFolder)await FilesystemTasks.Wrap(() => StorageFolder
						                                               .GetFolderFromPathAsync(unescapedValue)
						                                               .AsTask());
					if (folder is not null && !string.IsNullOrEmpty(folder.Path)) {
						// Convert short name to long name (#6190)
						unescapedValue = folder.Path;
					}

					switch (parsedArgs[0]) {
						case "tab":
							rootFrame.Navigate(typeof(MainPage),
							                   new MainPageNavigationArguments {
								                   Parameter = TabBarItemParameter.Deserialize(unescapedValue),
								                   IgnoreStartupSettings = true
							                   },
							                   new SuppressNavigationTransitionInfo());
							break;

						case "folder":
							rootFrame.Navigate(typeof(MainPage),
							                   new MainPageNavigationArguments {
								                   Parameter             = unescapedValue,
								                   IgnoreStartupSettings = true
							                   },
							                   new SuppressNavigationTransitionInfo());
							break;

						case "cmd":
							ParsedCommands ppm = CommandLineParser.ParseUntrustedCommands(unescapedValue);
							if (ppm.IsEmpty())
								rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
							else
								await InitializeFromCmdLineArgsAsync(rootFrame, ppm);
							break;
						default:
							rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
							break;
					}
				}

				break;

			case ICommandLineActivatedEventArgs cmdLineArgs:
				CommandLineActivationOperation? operation      = cmdLineArgs.Operation;
				string?                         cmdLineString  = operation.Arguments;
				string?                         activationPath = operation.CurrentDirectoryPath;

				ParsedCommands? parsedCommands = CommandLineParser.ParseUntrustedCommands(cmdLineString);
				if (parsedCommands is not null && parsedCommands.Count > 0) {
					await InitializeFromCmdLineArgsAsync(rootFrame, parsedCommands, activationPath);
				} else {
					rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
				}

				break;

			case IFileActivatedEventArgs fileArgs:
				int index = 0;
				if (rootFrame.Content is null             ||
				    rootFrame.Content is SplashScreenPage ||
				    !MainPageViewModel.AppInstances.Any()) {
					// When the navigation stack isn't restored navigate to the first page,
					// configuring the new page by passing required information as a navigation parameter
					rootFrame.Navigate(typeof(MainPage), fileArgs.Files.First().Path,
					                   new SuppressNavigationTransitionInfo());
					index = 1;
				} else {
					// Bring to foreground (#14730)
					Win32Helper.BringToForegroundEx(new HWND(this.WindowHandle));
				}

				for (; index < fileArgs.Files.Count; index++) {
					await NavigationHelpers.AddNewTabByPathAsync(typeof(ShellPanesPage), fileArgs.Files[index].Path,
					                                             true);
				}

				break;

			case IStartupTaskActivatedEventArgs startupArgs:
				// Just launch the app with no arguments
				rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
				break;

			default:
				// Just launch the app with no arguments
				rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
				break;
		}

		if (!this.AppWindow.IsVisible) {
			// When resuming the cached instance
			this.AppWindow.Show();
			Activate();
		}

		Win32Helper.BringToForegroundEx(new HWND(this.WindowHandle)); // here

		if (Windows.Win32.PInvoke.IsIconic(new HWND(this.WindowHandle)))
			Instance.Restore(); // Restore window if minimized
	}

	public void ShowSplashScreen() {
		Frame rootFrame = EnsureWindowIsInitialized();

		rootFrame.Navigate(typeof(SplashScreenPage));
	}

	private void EnsureEarlyWindow() {
		// Set PersistenceId
		this.PersistenceId = "FilesMainWindow";

		// Set minimum sizes
		this.MinHeight = 416;
		this.MinWidth  = 516;

		this.AppWindow.Title = "Files";
		this.AppWindow.SetIcon(AppLifecycleHelper.AppIconPath);
		this.AppWindow.TitleBar.ExtendsContentIntoTitleBar    = true;
		this.AppWindow.TitleBar.ButtonBackgroundColor         = Colors.Transparent;
		this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

		// Workaround for full screen window messing up the taskbar
		// https://github.com/microsoft/microsoft-ui-xaml/issues/8431
		// This property should only be set if the "Automatically hide the taskbar" in Windows 11,
		// or "Automatically hide the taskbar in desktop mode" in Windows 10 is enabled.
		// Setting this property when the setting is disabled will result in the taskbar overlapping the application
		if (AppLifecycleHelper.IsAutoHideTaskbarEnabled())
			Win32PInvoke.SetPropW(this.WindowHandle, "NonRudeHWND", new IntPtr(1));
	}

	private async Task InitializeFromCmdLineArgsAsync(Frame  rootFrame, ParsedCommands parsedCommands,
	                                                  string activationPath = "") {
		async Task PerformNavigationAsync(string payload, string selectItem = null) {
			if (!string.IsNullOrEmpty(payload)) {
				payload = Constants.UserEnvironmentPaths.ShellPlaces.Get(payload.ToUpperInvariant(), payload);
				StorageFolder? folder =
					(StorageFolder)await FilesystemTasks.Wrap(() => StorageFolder.GetFolderFromPathAsync(payload)
						                                         .AsTask());
				if (folder is not null && !string.IsNullOrEmpty(folder.Path))
					payload = folder.Path; // Convert short name to long name (#6190)
			}

			IGeneralSettingsService? generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>();

			double boundsWidth = 0;
			try {
				boundsWidth = this.Bounds.Width;
			} catch (Exception ex) {
				// Handle exception in case WinUI Windows is closed
				// (see https://github.com/files-community/Files/issues/15599)

				App.Logger.LogWarning(ex, ex.Message);
				return;
			}

			PaneNavigationArguments paneNavigationArgs = new PaneNavigationArguments {
				LeftPaneNavPathParam    = payload,
				LeftPaneSelectItemParam = selectItem,
				RightPaneNavPathParam =
					boundsWidth > Constants.UI.MultiplePaneWidthThreshold &&
					(generalSettingsService?.AlwaysOpenDualPaneInNewTab ?? false)
						? "Home"
						: null
			};

			if (rootFrame.Content is MainPage && MainPageViewModel.AppInstances.Any()) {
				// Bring to foreground (#14730)
				Win32Helper.BringToForegroundEx(new HWND(this.WindowHandle));

				await NavigationHelpers.AddNewTabByParamAsync(typeof(ShellPanesPage), paneNavigationArgs);
			} else
				rootFrame.Navigate(typeof(MainPage), paneNavigationArgs, new SuppressNavigationTransitionInfo());
		}

		foreach (ParsedCommand command in parsedCommands) {
			switch (command.Type) {
				case ParsedCommandType.OpenDirectory:
				case ParsedCommandType.OpenPath:
				case ParsedCommandType.ExplorerShellCommand:
					ParsedCommand? selectItemCommand =
						parsedCommands.FirstOrDefault(x => x.Type == ParsedCommandType.SelectItem);
					await PerformNavigationAsync(command.Payload, selectItemCommand?.Payload);
					break;

				case ParsedCommandType.SelectItem:
					if (IO.Path.IsPathRooted(command.Payload))
						await PerformNavigationAsync(IO.Path.GetDirectoryName(command.Payload),
						                             IO.Path.GetFileName(command.Payload));
					break;

				case ParsedCommandType.TagFiles:
					IFileTagsSettingsService? tagService = Ioc.Default.GetService<IFileTagsSettingsService>();
					TagViewModel?             tag        = tagService.GetTagsByName(command.Payload).FirstOrDefault();
					foreach (string file in command.Args.Skip(1)) {
						FilesystemResult<ulong?>? fileFRN = await FilesystemTasks
						                                         .Wrap(() =>
							                                               StorageHelpers
								                                              .ToStorageItem<IStorageItem>(file))
						                                         .OnSuccess(item => FileTagsHelper.GetFileFRN(item));
						if (fileFRN is not null) {
							string[] tagUid = tag is not null
								? new[] {
									tag.Uid
								}
								: [];
							FileTagsDatabase dbInstance = FileTagsHelper.GetDbInstance();
							dbInstance.SetTags(file, fileFRN, tagUid);
							FileTagsHelper.WriteFileTag(file, tagUid);
						}
					}

					break;

				case ParsedCommandType.Unknown:
					if (command.Payload.Equals(".")) {
						await PerformNavigationAsync(activationPath);
					} else {
						if (!string.IsNullOrEmpty(command.Payload)) {
							string target = IO.Path.GetFullPath(IO.Path.Combine(activationPath, command.Payload));
							await PerformNavigationAsync(target);
						} else {
							await PerformNavigationAsync(null);
						}
					}

					break;

				case ParsedCommandType.OutputPath:
					App.OutputPath = command.Payload;
					break;
			}
		}
	}

	/// <summary>
	/// Invoked when Navigation to a certain page fails
	/// </summary>
	/// <param name="sender">The Frame which failed navigation</param>
	/// <param name="e">Details about the navigation failure</param>
	private void OnNavigationFailed(object sender, NavigationFailedEventArgs e) {
		throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
	}
}