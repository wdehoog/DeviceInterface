﻿using System;
using LabNation.DeviceInterface.Devices;
using LabNation.Common;
using LabNation.DeviceInterface.DataSources;
using System.Threading;
using System.IO;
using System.Linq;
#if WINDOWS
using System.Windows.Forms;
#endif

//
// Copied SmartScopeConsole and modified it so it can save data to be imported in Sigrok.
// For now it collects Digital signals using the B-channel and saves them into a CSV file.
// The file is called "smartscope.csv".
// Trigger is set to L on D0 mode Single.
//

namespace SmartScopeSave
{
	class MainClass {
		/// <summary>
		/// The DeviceManager detects device connections
		/// </summary>
		static DeviceManager deviceManager;
		/// <summary>
		/// The scope used in here
		/// </summary>
		static IScope scope;
		static bool running = true;
		static bool loggingEnabled = false;

		// 4M 2M 1M 512K 256K 128K
		static uint[] AcqDepthData = new uint[] {
			4 * 1024 * 1024, 2 * 1024 * 1024, 1 * 1024 * 1024, 512 * 1024, 256 * 1024, 128 * 1024 };
		static int acqDepthIndex = 2;
		static uint acquisitionDepth = AcqDepthData[acqDepthIndex];

		static double[] AcqLengthData = new double[] {
			0.000001, 0.000002, 0.000005, 0.00001, 0.00002, 0.00005, 0.0001, 0.0002,
			0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2 };
		static int acqLengthIndex = 16;
		static double acquisitionLength = AcqLengthData[acqLengthIndex];

		static bool optInteractive = false;
		static DigitalTriggerValue digitalTriggerValue = DigitalTriggerValue.L;
		static DigitalChannel digitalTriggerChannel = DigitalChannel.Digi1;

		[STAThread]
		static void Main (string[] args)
		{
			for(int i=0;i<args.Length;i++) {
				switch(args[i]) {

					case "--analog":
						acquirer = new AnalogAcquirer();
						break;
					case "--digital":
						acquirer = new DigitalAcquirer();
						break;

					case "-i":
					case "--interactive":
						optInteractive = true;
						break;
					case "-n":
					case "--non-interactive":
						optInteractive = false;
						break;

					case "--csv":
						sampleSerializer = new Serializers.CSVSerializer();
						break;
					case "--vcd":
						sampleSerializer = new Serializers.VCDSerializer();
						break;

					case "--acq-depth":
						try {
							acquisitionDepth = UInt32.Parse(args[++i]);
							acqDepthIndex = -1;
						} catch(FormatException) {
							Console.WriteLine($"Unable to parse depth '{args[i]}'");
						}
						break;

					case "--acq-length":
						try {
							acquisitionLength = Double.Parse(args[++i]);
							acqLengthIndex = -1;
						} catch(FormatException) {
							Console.WriteLine($"Unable to parse length '{args[i]}'");
						}
						break;

					case "--trigger":
						try {
							string[] triggerParam = args[++i].Split(':');
							digitalTriggerChannel = parseDigitalChannel(triggerParam[0]);
							digitalTriggerValue = (DigitalTriggerValue)Enum.Parse(typeof(DigitalTriggerValue), triggerParam[1]);
						} catch(Exception) {
							Console.WriteLine($"Unable to parse channel and value from trigger param '{args[i]}'");
						}
						break;

					case "--file-name":
						sampleSerializer.setFileName(args[++i]);
						break;

					case "--enable-log":
						loggingEnabled = true;
						break;

					case "-h":
					case "--help":
					default:
						printHelp();
						return;
				}
			}

			//Open logger on console window
			FileLogger consoleLog = null;
			if(loggingEnabled)
				consoleLog = new FileLogger(new StreamWriter(Console.OpenStandardOutput()), LogLevel.INFO);
			Logger.Info("LabNation SmartScope Save");
			Logger.Info("---------------------------------");

			//Set up device manager with a device connection handler (see below)
			deviceManager = new DeviceManager (connectHandler);
			deviceManager.Start ();

			ConsoleKeyInfo cki = new ConsoleKeyInfo();
			while (running) {
#if WINDOWS
                Application.DoEvents();
#endif
				Thread.Sleep (100);

				if (Console.KeyAvailable) {
					cki = Console.ReadKey (true);
					HandleKey (cki);
				}
			}
      Logger.Info("Stopping device manager");
			deviceManager.Stop ();
			Logger.Info("Stopping Logger");
			if(loggingEnabled) consoleLog.Stop();
		}

		static void printHelp() {
			Console.WriteLine("Usage:");
			Console.WriteLine("  --analog              : perform acquisition of the two analog channels");
			Console.WriteLine("  --digital             : perform acquisition of the eight digital channels");
			Console.WriteLine("  --csv                 : save in Comma Seperated Values file (CSV) format");
			Console.WriteLine("  --vcd                 : save in Value Change Dump (VCD) format");
			Console.WriteLine("   -i");
			Console.WriteLine("  --interactive         : keep running after acquisition and allow to start another one");
			Console.WriteLine("   -n");
			Console.WriteLine("  --non-interactive     : exit after acquisition");
			Console.WriteLine("  --acq-depth <depth>   : acquisition depth");
			Console.WriteLine("  --acq-length <length> : acquisition length");
			Console.WriteLine("  --trigger <param>     : trigger channel [0..7] & value [LHFR], example: D1:L");
			Console.WriteLine("  --file-name <name>    : file name");
			Console.WriteLine("  --enable-log          : enable log and print it");
			Console.WriteLine("   -h");
			Console.WriteLine("  --help                : show this message");
			Console.WriteLine($"Defaults: vcd({sampleSerializer.getFileName()}), non-interactive, 1M, 0.2s, trigger: 1L");
		}

		static void connectHandler (IDevice dev, bool connected)
		{
			//Only accept devices of the IScope type (i.e. not IWaveGenerator)
			//and block out the fallback device (dummy scope)
			if (connected && dev is IScope && !(dev is DummyScope)) {
				Logger.Info ("Device connected of type " + dev.GetType ().Name + " with serial " + dev.Serial);
				scope = (IScope)dev;
				acquirer.configure();
			} else {
				scope = null;
			}
		}

		static void HandleKey(ConsoleKeyInfo k)
		{
			switch (k.Key) {
				case ConsoleKey.Q:
				case ConsoleKey.X:
        case ConsoleKey.Escape:
					// quit
					running = false;
					break;

				case ConsoleKey.R:
				case ConsoleKey.Enter:
					// replace sample
					Console.WriteLine("Collecting...");
					sampleSerializer.initialize();
					scope.Running = true;
					scope.CommitSettings();
					break;
				/*case ConsoleKey.A:
					// add sample
					sampleSerializer.reopen();
					scope.Running = true;
					scope.CommitSettings();
					break;*/
				default:
					switch(k.KeyChar) {
						case '[':
							acqDepthIndex--;
							if(acqDepthIndex < 0)
								acqDepthIndex = 0; // AcqDepthData.Length - 1;
							scope.AcquisitionDepthUserMaximum = AcqDepthData[acqDepthIndex];
							scope.AcquisitionDepth = AcqDepthData[acqDepthIndex];
							scope.CommitSettings();
							printScopeAcqConfig();
							break;
						case ']':
							acqDepthIndex++;
							if(acqDepthIndex >= AcqDepthData.Length)
								acqDepthIndex = AcqDepthData.Length - 1; // 0
							scope.AcquisitionDepthUserMaximum = AcqDepthData[acqDepthIndex];
							scope.AcquisitionDepth = AcqDepthData[acqDepthIndex];
							scope.CommitSettings();
							printScopeAcqConfig();
							break;
						case '<':
							acqLengthIndex--;
							if(acqLengthIndex < 0)
								acqLengthIndex = 0; // AcqDepthData.Length - 1;
							scope.AcquisitionLength = AcqLengthData[acqLengthIndex];
							scope.CommitSettings();
							printScopeAcqConfig();
							break;
						case '>':
							acqLengthIndex++;
							if(acqLengthIndex >= AcqLengthData.Length)
								acqLengthIndex = AcqLengthData.Length - 1; // 0
							scope.AcquisitionLength = AcqLengthData[acqLengthIndex];
							scope.CommitSettings();
							printScopeAcqConfig();
							break;
					}
					break;
			}
		}

		//static Serializers.ISampleSerializer sampleSerializer = new Serializers.CSVSerializer();
		static Serializers.ISampleSerializer sampleSerializer = new Serializers.VCDSerializer();

		public interface IAcquirer {
			void configure();
			void collectData(DataPackageScope p, DataSource s);
		}
		static IAcquirer acquirer = new DigitalAcquirer();

		public class AnalogAcquirer : IAcquirer {
			public void configure() {
				Logger.Info("Configuring scope");
				//Stop the scope acquisition (commit setting to device)
				scope.Running = false;
				scope.CommitSettings();

				//Set handler for new incoming data
				scope.DataSourceScope.OnNewDataAvailable += this.collectData;
				//Start datasource
				scope.DataSourceScope.Start();

				//Configure acquisition

				/******************************/
				/* Horizontal / time settings */
				/******************************/
				//Disable logic analyser
				scope.ChannelSacrificedForLogicAnalyser = null;
				//Don't use rolling mode
				scope.Rolling = false;
				//Don't fetch overview buffer for faster transfers
				scope.SendOverviewBuffer = false;
			  //Set sample depth to the minimum for a max datarate
			  //scope.AcquisitionLength = scope.AcquisitionLengthMin; 
				//trigger holdoff in seconds
				scope.TriggerHoldOff = 0;
				//Acquisition mode to Single since it does not work otherwise (wait for ever)
				scope.AcquisitionMode = AcquisitionMode.SINGLE; //AUTO;
				//Don't accept partial packages
				scope.PreferPartial = false;
				//Set viewport to match acquisition
				//scope.SetViewPort(0, scope.AcquisitionLength);

				scope.AcquisitionDepthUserMaximum = acquisitionDepth;
				scope.AcquisitionDepth = acquisitionDepth;
				scope.AcquisitionLength = acquisitionLength;
				//scope.SetViewPort(0, scope.AcquisitionLength);

				/*******************************/
				/* Vertical / voltage settings */
				/*******************************/
				foreach(AnalogChannel ch in AnalogChannel.List) {
					//FIRST set vertical range
					scope.SetVerticalRange(ch, -3, 3);
					//THEN set vertical offset (dicated by range)
					scope.SetYOffset(ch, 0);
					//use DC coupling
					scope.SetCoupling(ch, Coupling.DC);
					//and x10 probes
					ch.SetProbe(Probe.DefaultX10Probe);
				}

				// Set trigger to channel A
				scope.TriggerValue = new TriggerValue() {
					source = TriggerSource.Channel,
					channel = AnalogChannel.ChA,
					mode = TriggerMode.Edge,
					edge = TriggerEdge.FALLING,
					level = 1.0f
				};

				//Update the scope with the current settings
				scope.CommitSettings();

				//Show user what he did
				PrintScopeConfiguration();

				sampleSerializer.initialize();

				//Set scope running;
				scope.Running = true;
				scope.CommitSettings();
			}
			int c;
			public void collectData(DataPackageScope p, DataSource s) {
				int triesLeft = 20;
				while(triesLeft >= 0) {
					DataPackageScope dps = scope.GetScopeData();

					if(dps == null) {
						triesLeft--;
						continue;
					}

					if(dps.FullAcquisitionFetchProgress < 1f) {
						continue;
					}

					if(dps.GetData(ChannelDataSourceScope.Acquisition, AnalogChannel.List[0]) == null) {
						Console.Write("Timeout while waiting for scope data.\n");
						running = false;
						return;
					}

					ChannelData cd = dps.GetData(ChannelDataSourceScope.Acquisition, AnalogChannel.List[0]); // [0] for channel A
					float[] va = (float[])cd.array;
					cd = dps.GetData(ChannelDataSourceScope.Acquisition, AnalogChannel.List[1]); // [1] for channel B
					float[] vb = (float[])cd.array;

					float[] samples = new float[2];
					sampleSerializer.prepareForAnalogSamples(cd.samplePeriod, cd.timeOffset, getScopeMetaStrings());
					for(int i = 0; i < va.Length; i++) {
						samples[0] = va[i];
						samples[1] = vb[i];
						sampleSerializer.handleAnalogSamples(samples);
					}
					sampleSerializer.finalize();

					Console.Write(String.Format("Saved {0} samples using {1} records into file \"{2}\"\n",
						va.Length, sampleSerializer.getNumberOfSavedRecords(), sampleSerializer.getFileName()));

					if(optInteractive) {
						printScopeAcqConfig();
						Console.Write("'[Enter|R]':repeat, '[]':prev/next AcqDepth, '<>':prev/next AcqLength, 'Q|X|Esc' to Quit\n");
					} else
						running = false;
					return;
				}
			}
		}

		public class DigitalAcquirer : IAcquirer {
			public void configure() {
				Logger.Info("Configuring scope");

				//Stop the scope acquisition (commit setting to device)
				scope.Running = false;
				scope.CommitSettings();

				//Set handler for new incoming data
				scope.DataSourceScope.OnNewDataAvailable += this.collectData;
				//Start datasource
				scope.DataSourceScope.Start();

				//Configure acquisition

				/******************************/
				/* Horizontal / time settings */
				/******************************/
				//Disable logic analyser
				scope.ChannelSacrificedForLogicAnalyser = AnalogChannel.ChB;
				//Don't use rolling mode
				scope.Rolling = false;
				//Don't fetch overview buffer for faster transfers
				scope.SendOverviewBuffer = false;
				//trigger holdoff in seconds
				scope.TriggerHoldOff = 0;
				//Acquisition mode to Single since it does not work otherwise (wait for ever)
				scope.AcquisitionMode = AcquisitionMode.SINGLE;
				//Don't accept partial packages
				scope.PreferPartial = false;
				//Set viewport to match acquisition
				scope.SetViewPort(0, scope.AcquisitionLength);

				scope.AcquisitionDepthUserMaximum = acquisitionDepth;
				scope.AcquisitionDepth = acquisitionDepth;
				scope.AcquisitionLength = acquisitionLength;

				// Digital trigger 
				System.Collections.Generic.Dictionary<DigitalChannel, DigitalTriggerValue> digitalTriggers = scope.TriggerValue.Digital;
				digitalTriggers[digitalTriggerChannel] = digitalTriggerValue;
				scope.TriggerValue = new TriggerValue() {
					mode = TriggerMode.Digital,
					source = TriggerSource.Channel,
					channel = null,
					Digital = digitalTriggers
				};

				//Update the scope with the current settings
				scope.CommitSettings();

				//Show user what he did
				PrintScopeConfiguration();

				sampleSerializer.initialize();

				//Set scope runnign;
				scope.Running = true;
				scope.CommitSettings();
			}

			public void collectData(DataPackageScope p, DataSource s) {
				int triesLeft = 20;
				while(triesLeft >= 0) {
					DataPackageScope dps = scope.GetScopeData();

					if(dps == null) {
						triesLeft--;
						continue;
					}

					if(dps.FullAcquisitionFetchProgress < 1f) {
						continue;
					}

					if(dps.GetData(ChannelDataSourceScope.Acquisition, AnalogChannel.ChB.Raw()) != null) {
						ChannelData cd = dps.GetData(ChannelDataSourceScope.Acquisition, AnalogChannelRaw.List[1]); // [1] for channel B
						byte[] ba = (byte[])cd.array;
						sampleSerializer.prepareForLogicalSamples(cd.samplePeriod, cd.timeOffset, getScopeMetaStrings());
						foreach(byte b in ba) {
							sampleSerializer.handleLogicalSample(b);
						}
						sampleSerializer.finalize();

						Console.Write(String.Format("Saved {0} samples using {1} records into file \"{2}\"\n",
							ba.Length, sampleSerializer.getNumberOfSavedRecords(), sampleSerializer.getFileName()));

						if(optInteractive) {
							printScopeAcqConfig();
							//Console.Write("'R':replace, 'A':add, '[]':prev/next AcqDepth, 'Q|X|Esc' to Quit\n");
							Console.Write("'[Enter|R]':repeat, '[]':prev/next AcqDepth, '<>':prev/next AcqLength, 'Q|X|Esc' to Quit\n");
						} else
							running = false;

						return;
					}
				}
				Console.Write("Timeout while waiting for scope data.\n");
				running = false;
			}

		}

		static string[] getScopeMetaStrings() {
			return new string[] {
				String.Format("Acquisition Depth  : {0}", Utils.siPrint(scope.AcquisitionDepth, 1, 3, "Sa", 1024)),
				String.Format("Acquisition Length : {0}", Utils.siPrint(scope.AcquisitionLength, 1e-9, 3, "s")),
				String.Format("Sample Rate        : {0}", Utils.siPrint(1.0 / scope.SamplePeriod, 1, 3, "Hz"))
			};
		}

		static void printScopeAcqConfig() {
			Console.Write(String.Format("  Acq. Depth: {0}, Acq. Length: {1}, Sample rate: {2}\n",
							Utils.siPrint(scope.AcquisitionDepth, 1, 3, "Sa", 1024),
							Utils.siPrint(scope.AcquisitionLength, 1e-9, 3, "s"),
							Utils.siPrint(1.0 / scope.SamplePeriod, 1, 3, "Hz")));
		}

		static void PrintScopeConfiguration ()
		{
			string c = "";
			string f = "{0,-20}: {1:s}\n";
			c += "---------------------------------------------\n";
			c += "-              SCOPE SETTINGS               -\n";
			c += "---------------------------------------------\n";
			c += String.Format (f, "Scope serial", scope.Serial);
			c += String.Format (f, "Acquisition depth", Utils.siPrint (scope.AcquisitionDepth, 1, 3, "Sa", 1024));
			c += String.Format (f, "Acquisition length", Utils.siPrint (scope.AcquisitionLength, 1e-9, 3, "s"));
			c += String.Format (f, "Sample rate", Utils.siPrint (1.0 / scope.SamplePeriod, 1, 3, "Hz"));
			c += String.Format (f, "Viewport offset", Utils.siPrint (scope.ViewPortOffset, 1e-9, 3, "s"));
			c += String.Format (f, "Viewport timespan", Utils.siPrint (scope.ViewPortTimeSpan, 1e-9, 3, "s"));	
			c += String.Format (f, "Trigger holdoff", Utils.siPrint (scope.TriggerHoldOff, 1e-9, 3, "s"));
			c += String.Format (f, "Trigger channel", scope.TriggerValue.channel);
			//c += String.Format (f, "Trigger value", digitalTriggerValue); Exception!
			c += String.Format (f, "Acquisition mode", scope.AcquisitionMode.ToString ("G"));
			c += String.Format (f, "Rolling", scope.Rolling.YesNo ());
			c += String.Format (f, "Partial", scope.PreferPartial.YesNo ());
			c += String.Format (f, "Logic Analyser", scope.LogicAnalyserEnabled.YesNo ());


			string fCh = "Channel {0:s} - {1,-15:s}: {2:s}\n";
			foreach (AnalogChannel ch in AnalogChannel.List) {
				string chName = ch.Name;
				c += String.Format ("======= Channel {0:s} =======\n", chName);
				c += String.Format (fCh, chName, "Vertical offset", printVolt (scope.GetYOffset (ch)));
				c += String.Format (fCh, chName, "Coupling", scope.GetCoupling (ch).ToString ("G"));
				c += String.Format (fCh, chName, "Probe division", ch.Probe.ToString ());
			}
			c += "---------------------------------------------\n";
			Console.Write (c);				
		}

		static string printVolt(float v) {
			return Utils.siPrint(v, 0.013, 3, "V");
		}

		static DigitalChannel parseDigitalChannel(string dc) {
			dc = dc.ToUpper();
			if(dc == "D1")
				return DigitalChannel.Digi1;
			if(dc == "D2")
				return DigitalChannel.Digi2;
			if(dc == "D3")
				return DigitalChannel.Digi3;
			if(dc == "D4")
				return DigitalChannel.Digi4;
			if(dc == "D5")
				return DigitalChannel.Digi5;
			if(dc == "D6")
				return DigitalChannel.Digi6;
			if(dc == "D7")
				return DigitalChannel.Digi7;
			return DigitalChannel.Digi0;
		}
	}
}
