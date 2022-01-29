// SPDX-License-Identifier: GPL-2.0
// Compute thermodynamic properties of individual species and composition of species
// 
// Original C: Copyright (C) 2000
//    Antoine Lefebvre <antoine.lefebvre@polymtl.ca>
//    Mark Pinese  <pinese@cyberwizards.com.au>
//
// C# Port: Copyright (C) 2022
//    Ben Voß
// 
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;
using ImpulseRocketry.LibPropellantEval;

namespace ImpulseRocketry.Propep;

public class Program
{
    private const int MAX_CASE = 10;

    private static readonly string[] case_name = new string[]{
        "Fixed pressure-temperature equilibrium",
        "Fixed enthalpy-pressure equilibrium - adiabatic flame temperature",
        "Frozen equilibrium performance evaluation",
        "Shifting equilibrium performance evaluation"
    };

    private const string CHAMBER_MSG = "Time spent for computing chamber equilibrium";
    private const string FROZEN_MSG = "Time spent for computing frozen performance";
    private const string EQUILIBRIUM_MSG = "Time spent for computing equilibrium performance";

    public static int Main(string[] args)
    {
        var verboseOption = new Option<int>("v", "Verbosity setting, 0 - 10");
        verboseOption.AddValidator((s) => {
            var v = int.Parse(s.Tokens[0].Value);

            if (v < 0 || v > 10) {
                return "Verbose must be an integer between 0 and 10.";
            }
            return null;
        });

        var printPropellantList = new Command("p", "Print the propellant list.");
        printPropellantList.AddOption(verboseOption);
        printPropellantList.SetHandler<int>(verbose => PrintPropellantList(verbose), verboseOption);

        var printPropellantInfo = new Command("q", "Print information about propellant.");
        var componentNumberOption = new Option<int>("num", "component number");
        printPropellantInfo.AddOption(componentNumberOption);
        printPropellantInfo.AddOption(verboseOption);
        printPropellantInfo.SetHandler<int, int>((componentNumber, verbose) => PrintPropellantInfo(componentNumber, verbose), componentNumberOption, verboseOption);

        var printThermoList = new Command("t", "Print the combustion product list.");
        printThermoList.AddOption(verboseOption);
        printThermoList.SetHandler<int>(verbose => PrintThermoList(verbose), verboseOption);

        var printThermoInfo = new Command("u", "Print information about product.");
        var productNumberOption = new Option<int>("num", "product number");
        printThermoInfo.AddOption(productNumberOption);
        printThermoInfo.AddOption(verboseOption);
        printThermoInfo.SetHandler<int, int>((productNumber, verbose) => PrintThermoInfo(productNumber, verbose), productNumberOption, verboseOption);

        var welcomeInfo = new Command("i", "Print program information");
        welcomeInfo.SetHandler<int>(componentNumber => WelcomeInfo());

        var runCommand = new Command("f", "Perform an analysis of the propellant data in file.");
        var fileOption = new Option<string>("file", "The file");
        runCommand.AddOption(fileOption);
        runCommand.AddOption(verboseOption);
        runCommand.SetHandler<string, int>((file, verbose) => Run(file, verbose), fileOption, verboseOption);

        var cmd = new RootCommand
        {
            printPropellantList,
            printPropellantInfo,
            printThermoList,
            printThermoInfo,
            welcomeInfo,
            runCommand
        };

        return cmd.Invoke(args);
    }

    private static int WelcomeInfo()
    {

        var assembly = Assembly.GetExecutingAssembly();
        var versionAttribute = (AssemblyInformationalVersionAttribute)assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)[0];
        var versionAndDate = versionAttribute.InformationalVersion;

        Console.WriteLine("----------------------------------------------------------");
        Console.WriteLine("PropellantEval is a C# implementation of cpropep, the chemical");
        Console.WriteLine("equilibrium algorythm presented by GORDON and McBRIDE in the");
        Console.WriteLine("NASA report RP-1311.");
        Console.WriteLine($"This is the version {versionAndDate}");
        Console.WriteLine("This software is released under the GPL and is free of charge");
        Console.WriteLine("Cpropep, Copyright (C) 2000 Antoine Lefebvre <antoine.lefebvre@polymtl.ca>");
        Console.WriteLine("C# Port, Copyright (C) 2022 Ben Voß");
        Console.WriteLine("----------------------------------------------------------");

        return 0;
    }

    private static int PrintPropellantList(int verbose)
    {
        var propellantList = new PropellantList(verbose);
        propellantList.PrintList();

        return 0;
    }

    private static int PrintPropellantInfo(int componentNumber, int verbose)
    {
        var propellantList = new PropellantList(verbose);
        propellantList.PrintInfo(componentNumber);

        return 0;
    }

    private static int PrintThermoList(int verbose)
    {
        var thermoList = new ThermoList(verbose);
        thermoList.PrintList();
        return 0;
    }

    private static int PrintThermoInfo(int componentNumber, int verbose)
    {
        var thermoList = new ThermoList(verbose);
        thermoList.PrintInfo(componentNumber);
        return 0;
    }

    private static void LoadInput(PropellantList propellantList, FileInfo fileInfo, Equilibrium e, Case[] t)
    {
        // Open the file for reading
        using var fileStream = fileInfo.OpenRead();
        using var reader = new StreamReader(fileStream);

        var section = 0;
        var n_case = 0;

        do
        {
            var buffer = reader.ReadLine();
            if (buffer is null)
            {
                return;
            }

            switch (section)
            {
                case 0:
                    {
                        if (n_case >= MAX_CASE)
                        {
                            Console.WriteLine($"Warning: Too many different cases, maximum is {MAX_CASE + 1}: deleting case.");
                            section = 100;
                            break;
                        }

                        if (buffer.Length == 0 || buffer[0] == ' ' || buffer[0] == '\n' || buffer[0] == '#')
                        {
                            section = 0;
                            break;
                        }
                        else if (buffer.StartsWith("Propellant"))
                        {
                            section = 1;
                        }
                        //          else if (strncmp(buffer, "thermo_file", 10) == 0)
                        //          {
                        //            printf("New path...\n");
                        //            sscanf(buffer, "%s %s", variable, thermo_file);
                        //          }
                        //          else if (strncmp(buffer, "propellant.dat", 10) == 0)
                        //          {
                        //            printf("hehe\n");
                        //            sscanf(buffer, "%s %s", variable, propellant_file); 
                        //          }
                        else
                        {
                            if (buffer.StartsWith("TP"))
                            {
                                t[n_case].CaseType = CaseType.SimpleEquilibrium;
                            }
                            else if (buffer.StartsWith("HP"))
                            {
                                t[n_case].CaseType = CaseType.FindFlameTemperature;
                            }
                            else if (buffer.StartsWith("FR"))
                            {
                                t[n_case].CaseType = CaseType.FrozenPerformance;
                            }
                            else if (buffer.StartsWith("EQ"))
                            {
                                t[n_case].CaseType = CaseType.EquilibriumPerformance;
                            }
                            else
                            {
                                Console.WriteLine("Unknown option.");
                                break;
                            }
                            section = 2;
                        }

                        break;
                    }

                case 1:
                    {
                        // propellant section
                        if (buffer.Length == 0 || buffer[0] == ' ' || buffer[0] == '\n')
                        {
                            section = 0;
                        }
                        else if (buffer[0] == '+')
                        {
                            var parts = Split(buffer);
                            var num = parts[0];
                            var qt = parts[1];
                            var unit = parts[2];

                            var sp = int.Parse(num[1..]);
                            var m = double.Parse(qt);

                            if (unit == "g")
                            {
                                e.AddInPropellant(sp, propellantList.GramToMol(m, sp));
                            }
                            else if (unit == "m")
                            {
                                e.AddInPropellant(sp, m);
                            }
                            else
                            {
                                Console.WriteLine("Unit must be g (gram) or m (mol)");
                                break;
                            }
                            break;
                        }
                        else if (buffer[0] == '#')
                        {
                            break;
                        }

                        break;
                    }

                case 2:
                    {
                        if (buffer.Length == 0 || buffer[0] == ' ' || buffer[0] == '\n')
                        {
                            section = 0;
                            n_case++;
                        }
                        else if (buffer[0] == '+')
                        {
                            var parts = Split(buffer);
                            var variable = parts[0];
                            var qt = parts[1];
                            var unit = parts.Length > 2 ? parts[2] : string.Empty;

                            var bufptr = variable[1..];

                            if (bufptr == "chamber_temperature")
                            {
                                var m = double.Parse(qt);

                                if (unit == "k")
                                {
                                    t[n_case].Temperature = m;
                                }
                                else if (unit == "c")
                                {
                                    t[n_case].Temperature = m + 273.15;
                                }
                                else if (unit == "f")
                                {
                                    t[n_case].Temperature = (5.0 / 9.0) * (m - 32.0) + 273.15;
                                }
                                else
                                {
                                    Console.WriteLine("Unit must be k (kelvin) or c (celcius)");
                                    break;
                                }

                                t[n_case].IsTemperatureSet = true;

                            }
                            else if (bufptr == "chamber_pressure")
                            {
                                var m = double.Parse(qt);

                                if (unit == "atm")
                                {
                                    t[n_case].Pressure = m;
                                }
                                else if (unit == "kPa")
                                {
                                    t[n_case].Pressure = Conversion.KPA_TO_ATM * m;
                                }
                                else if (unit == "psi")
                                {
                                    t[n_case].Pressure = Conversion.PSI_TO_ATM * m;
                                }
                                else if (unit == "bar")
                                {
                                    t[n_case].Pressure = Conversion.BAR_TO_ATM * m;
                                }
                                else
                                {
                                    Console.Error.WriteLine("Units must be psi, kPa, atm or bar.");
                                    break;
                                }

                                t[n_case].IsPressureSet = true;
                            }
                            else if (bufptr == "exit_pressure")
                            {
                                var m = double.Parse(qt);

                                if (unit == "atm")
                                {
                                    t[n_case].ExitCondition = m;
                                }
                                else if (unit == "kPa")
                                {
                                    t[n_case].ExitCondition = Conversion.KPA_TO_ATM * m;
                                }
                                else if (unit == "psi")
                                {
                                    t[n_case].ExitCondition = Conversion.PSI_TO_ATM * m;
                                }
                                else if (unit == "bar")
                                {
                                    t[n_case].ExitCondition = Conversion.BAR_TO_ATM * m;
                                }
                                else
                                {
                                    Console.Error.WriteLine("Units must be psi, kPa, atm or bar.");
                                    break;
                                }

                                t[n_case].ExitConditionType = ExitCondition.PRESSURE;
                                t[n_case].IsExitConditionSet = true;

                            }
                            else if (bufptr == "supersonic_area_ratio")
                            {
                                t[n_case].ExitConditionType = ExitCondition.SUPERSONIC_AREA_RATIO;
                                t[n_case].ExitCondition = double.Parse(qt);
                                t[n_case].IsExitConditionSet = true;
                            }
                            else if (bufptr == "subsonic_area_ratio")
                            {
                                t[n_case].ExitConditionType = ExitCondition.SUBSONIC_AREA_RATIO;
                                t[n_case].ExitCondition = double.Parse(qt);
                                t[n_case].IsExitConditionSet = true;
                            }
                            else
                            {
                                Console.WriteLine("Unknown keyword.\n");
                                break;
                            }

                            break;
                        }
                        else if (buffer[0] == '#')
                        {
                            break;
                        }

                        break;
                    }

                default:
                    {
                        section = 0;
                        break;
                    }
            }
        } while (true);
    }

    private static string[] Split(string input)
    {
        return input.Split(' ').Where(s => s != "").ToArray();
    }

    private static void Time(Action action, string msg)
    {
        //var s = Stopwatch.StartNew();

        action();

        // Console.WriteLine($"{msg}: {s.Elapsed}");
        //Console.WriteLine();
    }

    private static int Run(string fileName, int verbose)
    {
        int i;
        var caseList = new Case[MAX_CASE];
        for (i = 0; i < MAX_CASE; i++)
        {
            caseList[i] = new Case
            {
                CaseType = (CaseType)(-1),
                IsTemperatureSet = false,
                IsPressureSet = false,
                IsExitConditionSet = false
            };
        }

        var evaluator = new Evaluator(verbose);

        var equil = new Equilibrium();

        var frozen = new Equilibrium[3];
        var shifting = new Equilibrium[3];
        for (i = 0; i < 3; i++)
        {
            frozen[i] = new Equilibrium();
            shifting[i] = new Equilibrium();
        }

        var fileInfo = new FileInfo(fileName);
        LoadInput(evaluator.PropellantList, fileInfo, equil, caseList);

        evaluator.ComputeDensity(equil.Propellant);

        evaluator.ListElement(equil);
        evaluator.ListProduct(equil);

        i = 0;
        while (((int)caseList[i].CaseType != -1) && (i <= MAX_CASE))
        {
            Console.WriteLine($"Computing case {i + 1}");
            Console.WriteLine(case_name[(int)caseList[i].CaseType]);
            Console.WriteLine();

            // be sure to begin iteration without considering
            // condensed species. Once n_condensed have been set
            equil.Product.NumSpecies[Constants.CONDENSED] = 0;

            switch (caseList[i].CaseType)
            {
                case CaseType.SimpleEquilibrium:
                    {

                        if (!caseList[i].IsTemperatureSet)
                        {
                            Console.WriteLine("Chamber temperature not set. Aborted.");
                            break;
                        }
                        else if (!caseList[i].IsPressureSet)
                        {
                            Console.WriteLine("Chamber pressure not set. Aborted.");
                            break;
                        }

                        equil.Properties.T = caseList[i].Temperature;
                        equil.Properties.P = caseList[i].Pressure;

                        evaluator.PrintPropellantComposition(equil);
                        Time(() => evaluator.Equilibrium(equil, ProblemType.TP), CHAMBER_MSG);

                        Evaluator.PrintProductProperties(equil);
                        evaluator.PrintProductComposition(equil);
                        break;
                    }

                case CaseType.FindFlameTemperature:
                    {

                        if (!caseList[i].IsPressureSet)
                        {
                            Console.WriteLine("Chamber pressure not set. Aborted.");
                            break;
                        }

                        equil.Properties.P = caseList[i].Pressure;

                        evaluator.PrintPropellantComposition(equil);
                        Time(() => evaluator.Equilibrium(equil, ProblemType.HP), CHAMBER_MSG);

                        Evaluator.PrintProductProperties(equil);
                        evaluator.PrintProductComposition(equil);
                        break;
                    }

                case CaseType.FrozenPerformance:
                    {

                        if (!caseList[i].IsPressureSet)
                        {
                            Console.WriteLine("Chamber pressure not set. Aborted.");
                            break;
                        }
                        else if (!caseList[i].IsExitConditionSet)
                        {
                            Console.WriteLine("Exit condition not set. Aborted.");
                            break;
                        }

                        equil.Properties.T = caseList[i].Temperature;
                        equil.Properties.P = caseList[i].Pressure;

                        equil.CopyTo(frozen[0]);

                        evaluator.PrintPropellantComposition(frozen[0]);

                        Time(() => evaluator.Equilibrium(frozen[0], ProblemType.HP), CHAMBER_MSG);

                        Time(() => evaluator.FrozenPerformance(frozen, caseList[i].ExitConditionType,
                                                caseList[i].ExitCondition), FROZEN_MSG);

                        Evaluator.PrintProductProperties(frozen);
                        Evaluator.PrintPerformanceInformation(frozen, 3);
                        evaluator.PrintProductComposition(frozen);

                        break;
                    }

                case CaseType.EquilibriumPerformance:
                    {

                        if (!caseList[i].IsPressureSet)
                        {
                            Console.WriteLine("Chamber pressure not set. Aborted.");
                            break;
                        }
                        else if (!caseList[i].IsExitConditionSet)
                        {
                            Console.WriteLine("Exit condition not set. Aborted.");
                            break;
                        }

                        equil.Properties.T = caseList[i].Temperature;
                        equil.Properties.P = caseList[i].Pressure;

                        equil.CopyTo(shifting[0]);

                        evaluator.PrintPropellantComposition(shifting[0]);

                        Time(() => evaluator.Equilibrium(shifting[0], ProblemType.HP), CHAMBER_MSG);

                        Time(() => evaluator.ShiftingPerformance(shifting, caseList[i].ExitConditionType,
                                                caseList[i].ExitCondition), EQUILIBRIUM_MSG);

                        Evaluator.PrintProductProperties(shifting);
                        Evaluator.PrintPerformanceInformation(shifting, 3);
                        evaluator.PrintProductComposition(shifting);

                        break;
                    }
            }

            i++;
        }

        return 0;
    }
}
