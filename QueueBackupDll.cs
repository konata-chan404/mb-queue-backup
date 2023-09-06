using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using MusicBeePlugin;
using System.Text;

namespace MusicBeePlugin
{
    [Serializable]
    public class QueueBackup
    {
        public List<string> TrackUrls { get; set; }

        public QueueBackup()
        {
            TrackUrls = new List<string>();
        }

        public override string ToString()
        {
            // Convert the class data to a string representation
            string s = "";
            foreach (string url in TrackUrls)
            {
                s = s + url + Environment.NewLine;
            }
            return s;
        }

        public static QueueBackup FromString(string data)
        {
            // Parse the string representation back into a QueueBackup instance
            QueueBackup backup = new QueueBackup();
            string[] lines = data.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            backup.TrackUrls.AddRange(lines);
            return backup;
        }
    }

    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        private string backupFolderPath;
        private List<QueueBackup> backups = new List<QueueBackup>();
        private bool isConfig = false;
        private bool isEdit = false;
        private DateTime lastEdit;

        private double cleanupTimeInHours = 24.0; // Default cleanup time is 24 hours
        private double EditTimeInMinutes = 1;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "˚ʚ♡ɞ˚ Backup Queue Helper ˚ʚ♡ɞ˚";
            about.Description = "ily<3";
            about.Author = "YaelFluff";
            about.TargetApplication = "";   //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;
            about.VersionMajor = 1;  // your plugin version
            about.VersionMinor = 0;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            // Initialize backup folder path
            backupFolderPath = Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), "QueueBackups");

            // Create the backup folder if it doesn't exist
            Directory.CreateDirectory(backupFolderPath);

            // Load the settings from the text file if it exists
            string settingsFilePath = Path.Combine(mbApiInterface.Setting_GetPersistentStoragePath(), "BackupQueueSettings");
            if (File.Exists(settingsFilePath))
            {
                string[] settings = File.ReadAllLines(settingsFilePath);
                if (settings.Length >= 2 && double.TryParse(settings[0], out double parsedCleanupTime) && parsedCleanupTime > 0)
                {
                    cleanupTimeInHours = parsedCleanupTime;
                }
                if (settings.Length >= 2 && double.TryParse(settings[1], out double parsedEditTime) && parsedEditTime >= 0)
                {
                    EditTimeInMinutes = parsedEditTime;
                }
            }

            // Schedule the cleanup task and backup task
            ScheduleBackupCleanup();

            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // Save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            isConfig = true;

            // Create a new popup form to display the available backups and the cleanup time configuration
            Form popupForm = new Form();
            popupForm.Text = "Choose Backup and Configure Cleanup Time";
            popupForm.Width = 500;
            popupForm.Height = 400;
            popupForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            popupForm.StartPosition = FormStartPosition.CenterScreen;

            popupForm.FormClosed += (sender, e) =>
            {
                isConfig = false;
            };

            // Get a list of all backup files in the backup folder
            string[] backupFiles = Directory.GetFiles(backupFolderPath);

            // Create a list box to display the backup names
            ListBox listBoxBackups = new ListBox();
            listBoxBackups.Dock = DockStyle.Top;
            listBoxBackups.Height = 150;
            listBoxBackups.SelectedIndexChanged += (sender, e) =>
            {
                // When the user selects a backup, call the RestoreQueue method
                string selectedBackup = listBoxBackups.SelectedItem.ToString();
                RestoreQueue(selectedBackup);

            };

            // Add the backup names to the list box
            foreach (string backupFile in backupFiles)
            {
                listBoxBackups.Items.Add(Path.GetFileName(backupFile));
            }

            // Add the list box to the popup form
            popupForm.Controls.Add(listBoxBackups);

            // Add a group box for settings configuration
            GroupBox groupBoxSettings = new GroupBox();
            groupBoxSettings.Text = "Settings Configuration";
            groupBoxSettings.Dock = DockStyle.Bottom;
            groupBoxSettings.Height = 150;

            Label lblCleanupTime = new Label();
            lblCleanupTime.Text = "Cleanup Time:";
            lblCleanupTime.Location = new Point(10, 20);

            TextBox txtCleanupTime = new TextBox();
            txtCleanupTime.Text = cleanupTimeInHours.ToString();
            txtCleanupTime.Location = new Point(150, 20);

            Label lblEditTime = new Label();
            lblEditTime.Text = "Edit Time:";
            lblEditTime.Location = new Point(10, 60);

            TextBox txtEditTime = new TextBox();
            txtEditTime.Text = EditTimeInMinutes.ToString();
            txtEditTime.Location = new Point(150, 60);

            Button btnSaveSettings = new Button();
            btnSaveSettings.Text = "Save";
            btnSaveSettings.Location = new Point(150, 100);
            btnSaveSettings.Click += (sender, e) =>
            {
                // Save the settings to the text file
                if (double.TryParse(txtCleanupTime.Text, out double newCleanupTime) && newCleanupTime > 0 &&
                    double.TryParse(txtEditTime.Text, out double newEditTime) && newEditTime >= 0)
                {
                    cleanupTimeInHours = newCleanupTime;
                    EditTimeInMinutes = newEditTime;
                    SaveSettings();
                    popupForm.Close();
                }
                else
                {
                    MessageBox.Show("Invalid input. Please enter valid values for cleanup time and edit time.");
                }
            };

            groupBoxSettings.Controls.Add(lblCleanupTime);
            groupBoxSettings.Controls.Add(txtCleanupTime);
            groupBoxSettings.Controls.Add(lblEditTime);
            groupBoxSettings.Controls.Add(txtEditTime);
            groupBoxSettings.Controls.Add(btnSaveSettings);

            // Add the group box to the popup form
            popupForm.Controls.Add(groupBoxSettings);

            // Show the popup form
            popupForm.ShowDialog();

            return true;
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            DateTime now = DateTime.Now;
            TimeSpan diff = now - lastEdit;
            if (diff.TotalMinutes > EditTimeInMinutes && isEdit && !isConfig)
            {
                BackupQueue(now.ToString("[yyyy-MM-dd] HH mm ss"));
                lastEdit = now;
                isEdit = false;
            }
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PlayingTracksChanged:
                    isEdit = true;
                    break;
            }
        }

        public void BackupQueue(string backupName)
        {
            if (mbApiInterface.NowPlayingList_QueryFilesEx(null, out string[] files))
            {
                string backupFilePath = Path.Combine(backupFolderPath, backupName);

                // Use StringBuilder to efficiently build the string representation
                StringBuilder backupData = new StringBuilder();
                foreach (string fileUrl in files)
                {
                    backupData.AppendLine(fileUrl);
                }

                File.WriteAllText(backupFilePath, backupData.ToString());
            }
        }

        public void RestoreQueue(string backupName)
        {
            string backupFilePath = Path.Combine(backupFolderPath, backupName);
            if (File.Exists(backupFilePath))
            {
                string[] backupData = File.ReadAllLines(backupFilePath);

                // Clear the current Now Playing list
                mbApiInterface.NowPlayingList_Clear();
                mbApiInterface.NowPlayingList_QueueFilesLast(backupData);
            }
        }

        private void ScheduleBackupCleanup()
        {
            // Cleanup old backups based on the cleanup time in hours
            DateTime cutoffTime = DateTime.Now.AddHours(-cleanupTimeInHours);
            DirectoryInfo backupDir = new DirectoryInfo(backupFolderPath);
            FileInfo[] backupFiles = backupDir.GetFiles();

            foreach (FileInfo backupFile in backupFiles)
            {
                if (backupFile.LastWriteTime < cutoffTime)
                {
                    File.Delete(backupFile.FullName);
                }
            }
        }


        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();

            // Save the settings to the text file
            string settingsFilePath = Path.Combine(dataPath, "BackupQueueSettings");
            string settingsData = cleanupTimeInHours.ToString() + Environment.NewLine + EditTimeInMinutes.ToString();
            File.WriteAllText(settingsFilePath, settingsData);
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }
    }
}