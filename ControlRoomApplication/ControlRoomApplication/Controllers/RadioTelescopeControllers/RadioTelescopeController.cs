﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ControlRoomApplication.Constants;
using ControlRoomApplication.Entities;
using System.Threading;
using ControlRoomApplication.Controllers.Sensors;
using ControlRoomApplication.Database;
using ControlRoomApplication.Controllers.Communications;

namespace ControlRoomApplication.Controllers
{
    public class RadioTelescopeController
    {
        public RadioTelescope RadioTelescope { get; set; }
        public CoordinateCalculationController CoordinateController { get; set; }
        private bool tempAcceptable = true;
        public OverrideSwitchData overrides;

        // Thread that monitors database current temperature
        Thread tempM;

        private static readonly log4net.ILog logger =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Constructor that takes an AbstractRadioTelescope object and sets the
        /// corresponding field.
        /// </summary>
        /// <param name="radioTelescope"></param>
        public RadioTelescopeController(RadioTelescope radioTelescope)
        {
            RadioTelescope = radioTelescope;
            CoordinateController = new CoordinateCalculationController(radioTelescope.Location);

            overrides = new OverrideSwitchData();

            tempM = new Thread(tempMonitor);
            tempM.Start();


        }

        /// <summary>
        /// Gets the status of whether this RT is responding.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        /// <returns> Whether or not the RT responded. </returns>
        public bool TestCommunication()
        {
            return RadioTelescope.PLCDriver.Test_Connection();
        }

        /// <summary>
        /// Gets the current orientation of the radiotelescope in azimuth and elevation.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        /// <returns> An orientation object that holds the current azimuth/elevation of the scale model. </returns>
        public Orientation GetCurrentOrientation()
        {
            return RadioTelescope.PLCDriver.read_Position();
        }



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Orientation GetAbsoluteOrientation()
        {
            return RadioTelescope.Encoders.GetCurentOrientation();
        }

        /// <summary>
        /// Gets the status of the interlock system associated with this Radio Telescope.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        /// <returns> Returns true if the safety interlock system is still secured, false otherwise. </returns>
        public bool GetCurrentSafetyInterlockStatus()
        {
            return RadioTelescope.PLCDriver.Get_interlock_status();
        }

        /// <summary>
        /// Method used to cancel this Radio Telescope's current attempt to change orientation.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool CancelCurrentMoveCommand()
        {
            return RadioTelescope.PLCDriver.Cancel_move();
        }

        /// <summary>
        /// Method used to shutdown the Radio Telescope in the case of inclement
        /// weather, maintenance, etcetera.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool ShutdownRadioTelescope()
        {
            return RadioTelescope.PLCDriver.Shutdown_PLC_MCU();
        }

        /// <summary>
        /// Method used to calibrate the Radio Telescope before each observation.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public Task<bool> ThermalCalibrateRadioTelescope()
        {
            if (!tempAcceptable) return Task.FromResult(false);
            return RadioTelescope.PLCDriver.Thermal_Calibrate(); // MOVE
        }

        /// <summary>
        /// Method used to request to set configuration of elements of the RT.
        /// takes the starting speed of the motor in RPM (speed of tellescope after gearing)
        /// </summary>
        /// <param name="startSpeedAzimuth">RPM</param>
        /// <param name="startSpeedElevation">RPM</param>
        /// <param name="homeTimeoutAzimuth">SEC</param>
        /// <param name="homeTimeoutElevation">SEC</param>
        /// <returns></returns>
        public bool ConfigureRadioTelescope(double startSpeedAzimuth, double startSpeedElevation, int homeTimeoutAzimuth, int homeTimeoutElevation)
        {
            return RadioTelescope.PLCDriver.Configure_MCU(startSpeedAzimuth, startSpeedElevation, homeTimeoutAzimuth, homeTimeoutElevation); // NO MOVE
        }

        /// <summary>
        /// Method used to request to move the Radio Telescope to an objective
        /// azimuth/elevation orientation.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// <see cref="Controllers.BlkHeadUcontroler.EncoderReader"/>
        /// </summary>
        public Task<bool> MoveRadioTelescopeToOrientation(Orientation orientation)//TODO: once its intagrated use the microcontrole to get the current opsition 
        {
            if (!tempAcceptable) return Task.FromResult(false);
            return RadioTelescope.PLCDriver.Move_to_orientation(orientation, RadioTelescope.PLCDriver.read_Position()); // MOVE
        }

        /// <summary>
        /// Method used to request to move the Radio Telescope to an objective
        /// right ascension/declination coordinate pair.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public Task<bool> MoveRadioTelescopeToCoordinate(Coordinate coordinate)
        {
            if (!tempAcceptable) return Task.FromResult(false);
            return MoveRadioTelescopeToOrientation(CoordinateController.CoordinateToOrientation(coordinate, DateTime.UtcNow)); // MOVE
        }


        /// <summary>
        /// Method used to request to start jogging the Radio Telescope's azimuth
        /// at a speed (in RPM), in either the clockwise or counter-clockwise direction.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool StartRadioTelescopeAzimuthJog(double speed, bool PositiveDIR)
        {
            if (!tempAcceptable) return false;
            return RadioTelescope.PLCDriver.Start_jog( speed, PositiveDIR, 0,false );// MOVE
        }

        /// <summary>
        /// Method used to request to start jogging the Radio Telescope's elevation
        /// at a speed (in RPM), in either the clockwise or counter-clockwise direction.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool StartRadioTelescopeElevationJog(double speed, bool PositiveDIR)
        {
            if (!tempAcceptable) return false;
            return RadioTelescope.PLCDriver.Start_jog( 0,false,speed, PositiveDIR);// MOVE
        }


        /// <summary>
        /// send a clear move to the MCU to stop a jog
        /// </summary>
        public bool ExecuteRadioTelescopeStopJog() {
            return RadioTelescope.PLCDriver.Stop_Jog();
        }

        /// <summary>
        /// Method used to request that all of the Radio Telescope's movement comes
        /// to a controlled stop. this will not work for jog moves use 
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool ExecuteRadioTelescopeControlledStop()
        {
            return RadioTelescope.PLCDriver.Controled_stop(); // NO MOVE
        }

        /// <summary>
        /// Method used to request that all of the Radio Telescope's movement comes
        /// to an immediate stop.
        /// 
        /// The implementation of this functionality is on a "per-RT" basis, as
        /// in this may or may not work, it depends on if the derived
        /// AbstractRadioTelescope class has implemented it.
        /// </summary>
        public bool ExecuteRadioTelescopeImmediateStop()
        {
            return RadioTelescope.PLCDriver.Immediade_stop(); // NO MOVE
        }


        /// <summary>
        /// return true if the RT has finished the previous move comand
        /// </summary>
        public bool finished_exicuting_move( RadioTelescopeAxisEnum axis )//[7]
        {
             
            var Taz = RadioTelescope.PLCDriver.GET_MCU_Status( RadioTelescopeAxisEnum.AZIMUTH );  //Task.Run( async () => { await  } );
            var Tel = RadioTelescope.PLCDriver.GET_MCU_Status( RadioTelescopeAxisEnum.ELEVATION );

            Taz.Wait();
            bool azFin = Taz.Result[(int)MCUConstants.MCUStutusBitsMSW.Move_Complete];
            bool elFin = Tel.GetAwaiter().GetResult()[(int)MCUConstants.MCUStutusBitsMSW.Move_Complete];
            if(axis == RadioTelescopeAxisEnum.BOTH) {
                return elFin && azFin;
            } else if(axis == RadioTelescopeAxisEnum.AZIMUTH) {
                return azFin;
            } else if(axis == RadioTelescopeAxisEnum.ELEVATION) {
                return elFin;
            }
            return false;
        }


        private static bool ResponseMetBasicExpectations(byte[] ResponseBytes, int ExpectedSize)
        {
            return ((ResponseBytes[0] + (ResponseBytes[1] * 256)) == ExpectedSize) && (ResponseBytes[2] == 0x1);
            //TODO: throws object is not instance of object when the  PLCClientCommunicationHandler.ReadResponse() retuns null usually due to time out

         }

        private static bool MinorResponseIsValid(byte[] MinorResponseBytes)
        {
            
            System.Diagnostics.Debug.WriteLine(MinorResponseBytes);
            return ResponseMetBasicExpectations(MinorResponseBytes, 0x3);
        }

        // Checks the motor temperatures against acceptable ranges every second
        private void tempMonitor()
        {
            // Getting initial current temperatures
            Temperature currAZ = DatabaseOperations.GetCurrentTemp(SensorLocationEnum.AZ_MOTOR);
            bool AZ = checkTemp(currAZ);

            Temperature currEL = DatabaseOperations.GetCurrentTemp(SensorLocationEnum.EL_MOTOR);
            bool EL = checkTemp(currEL);

            // Loop through every one second to get new temperatures. If the temperature has changed, notify the user
            while (true)
            {
                // Only updates the info if the temperature has changed
                if (currAZ.temp != DatabaseOperations.GetCurrentTemp(SensorLocationEnum.AZ_MOTOR).temp) {
                    currAZ = DatabaseOperations.GetCurrentTemp(SensorLocationEnum.AZ_MOTOR);
                    AZ = checkTemp(currAZ);
                }

                if (currEL.temp != DatabaseOperations.GetCurrentTemp(SensorLocationEnum.EL_MOTOR).temp)
                {
                    currEL = DatabaseOperations.GetCurrentTemp(SensorLocationEnum.EL_MOTOR);
                    EL = checkTemp(currEL);
                }

                // Determines if the temperature is acceptable for both motors
                if (AZ && EL) tempAcceptable = true;
                else tempAcceptable = false;
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        ///  Checks that the motor temperatures are within acceptable ranges. If the temperature exceeds
        ///  the corresponding value in SimulationConstants.cs, it will return false, otherwise
        ///  it will return true if everything is good.
        ///  Tl;dr:
        ///  False - bad
        ///  True - good
        /// </summary>
        /// <returns>override bool</returns>
        public bool checkTemp(Temperature t)
        {
            EmailFields.setSender("system@ycpradiotelescope.com");
            // get maximum temperature threshold
            double max;

            // Determine whether azimuth or elevation
            String s;
            bool b;
            if (t.location_ID == (int)SensorLocationEnum.AZ_MOTOR)
            {
                s = "Azimuth";
                b = overrides.overrideAzimuthMotTemp;
                max = DatabaseOperations.GetThresholdForSensor(SensorItemEnum.AZ_MOTOR_TEMP);
            }
            else
            {
                s = "Elevation";
                b = overrides.overrideElevatMotTemp;
                max = DatabaseOperations.GetThresholdForSensor(SensorItemEnum.ELEV_MOTOR_TEMP);
            }

            EmailFields.setSubject("MOTOR TEMPERATURE");
            // Check temperatures
            if (t.temp < SimulationConstants.STABLE_MOTOR_TEMP)
            {
                logger.Info(s + " motor temperature BELOW stable temperature by " + Math.Truncate(SimulationConstants.STABLE_MOTOR_TEMP - t.temp) + " degrees Fahrenheit.");
                EmailFields.setText($"MOTOR TEMPERATURE\r\n{s} motor temperature BELOW stable temperature by {Math.Truncate(SimulationConstants.STABLE_MOTOR_TEMP - t.temp)} degrees Fahrenheit.");
                EmailFields.setHtml($@"<h1>MOTOR TEMPERATURE</h1>
                <p>{s} motor temperature BELOW stable temperature by {Math.Truncate(SimulationConstants.STABLE_MOTOR_TEMP - t.temp)} degrees Fahrenheit.</p>");
                pushNotification.send("MOTOR TEMPERATURE", s + " motor temperature BELOW stable temperature by " + Math.Truncate(SimulationConstants.STABLE_MOTOR_TEMP - t.temp) + " degrees Fahrenheit.");
                pushNotification.sendEmail();
                // Only overrides if switch is true
                if (!b) return false;
                else return true;
            }
            else if (t.temp > max)
            {
                logger.Info(s + " motor temperature OVERHEATING by " + Math.Truncate(t.temp - max) + " degrees Fahrenheit.");
                EmailFields.setText($"MOTOR TEMPERATURE\r\n{s} motor temperature OVERHEATING by {Math.Truncate(t.temp - max)} degrees Fahrenheit.");
                EmailFields.setHtml($@"<h1>MOTOR TEMPERATURE</h1>
                <p>{s} motor temperature OVERHEATING by {Math.Truncate(t.temp - max)} degrees Fahrenheit.</p>");
                pushNotification.send("MOTOR TEMPERATURE", s + " motor temperature OVERHEATING by " + Math.Truncate(t.temp - max) + " degrees Fahrenheit.");
                pushNotification.sendEmail();

                // Only overrides if switch is true
                if (!b) return false;
                else return true;
            }
            logger.Info(s + " motor temperature stable.");
            EmailFields.setText($"MOTOR TEMPERATURE\r\n{s} motor temperature stable.");
            EmailFields.setHtml($@"<h1>MOTOR TEMPERATURE</h1>
            <p>{s} motor temperature stable.</p>");
            pushNotification.send("MOTOR TEMPERATURE", s + " motor temperature stable.");
            pushNotification.sendEmail();

            return true;
        }

        /// <summary>
        /// This will set the overrides based on input. Takes in the sensor that it will be changing,
        /// and then the status, true or false.
        /// true = overriding
        /// false = enabled
        /// </summary>
        /// <param name="sensor"></param>
        /// <param name="set"></param>
        public void setOverride(String sensor, bool set)
        {
            if (sensor.Equals("azimuth motor temperature")) overrides.overrideAzimuthMotTemp = set;
            else if (sensor.Equals("elevation motor temperature")) overrides.overrideElevatMotTemp = set;
            else if (sensor.Equals("main gate"))
            {
                overrides.overrideGate = set;
                RadioTelescope.PLCDriver.setregvalue((ushort)PLC_modbus_server_register_mapping.GATE_OVERRIDE, Convert.ToUInt16(set));
            }
            else if (sensor.Equals("elevation proximity (2)")) {
                overrides.overrideElevatProx2 = set;
                RadioTelescope.PLCDriver.setregvalue((ushort)PLC_modbus_server_register_mapping.EL_90_LIMIT, Convert.ToUInt16(set));
            }
            else if (sensor.Equals("elevation proximity (1)"))
            {
                overrides.overrideElevatProx1 = set;
                RadioTelescope.PLCDriver.setregvalue((ushort)PLC_modbus_server_register_mapping.EL_10_LIMIT, Convert.ToUInt16(set));
            }
            else if (sensor.Equals("azimuth proximity (2)"))
            {
                overrides.overrideAzimuthProx2 = set;
                RadioTelescope.PLCDriver.setregvalue((ushort)PLC_modbus_server_register_mapping.AZ_375_LIMIT, Convert.ToUInt16(set));

            }
            else if (sensor.Equals("azimuth proximity (1)"))
            {
                overrides.overrideAzimuthProx1 = set;
                RadioTelescope.PLCDriver.setregvalue((ushort)PLC_modbus_server_register_mapping.AZ_0_LIMIT, Convert.ToUInt16(set));
            }

            EmailFields.setSender("system@ycpradiotelescope.com");
            EmailFields.setSubject("SENSOR ORVERRIDES");

            if (set)
            {
                logger.Info("Overriding " + sensor + " sensor.");
                EmailFields.setText($"SENSOR OVERRIDES\r\nOverriding {sensor} sensor.");
                EmailFields.setHtml($@"<h1>SENSOR OVERRIDES</h1>
                <p>Overriding {sensor} sensor.</p>");
                pushNotification.send("SENSOR OVERRIDES", "Overriding " + sensor + " sensor.");
                pushNotification.sendEmail();
            }
            else
            {
                logger.Info("Enabled " + sensor + " sensor.");
                EmailFields.setText($"SENSOR OVERRIDES\r\nEnabled {sensor} sensor.");
                EmailFields.setHtml($@"<h1>SENSOR OVERRIDES</h1>
                <p>Enabled {sensor} sensor.</p>");
                pushNotification.send("SENSOR OVERRIDES", "Enabled " + sensor + " sensor.");
            }
        }
    }
}