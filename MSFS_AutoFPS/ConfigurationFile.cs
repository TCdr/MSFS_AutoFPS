using System.Collections.Generic;
using System.IO;
using System.Security.RightsManagement;
using System.Windows;
using System.Xml;

namespace MSFS_AutoFPS
{
    public class ConfigurationFile
    {
        private Dictionary<string, string> appSettings = new();
        private XmlDocument xmlDoc = new();
        private string ConfigFile = App.ConfigFile;
        private string ConfigFileLast = App.ConfigFile;
        private bool isLoaded = false;

        public string this[string key]
        {
            get => GetSetting(key);
            set => SetSetting(key, value);
        }

        public bool LoadConfiguration(bool isSim2024)
        {
            string targetConfigFile = isSim2024 ? App.ConfigFile2024 : App.ConfigFile;

            // This class is the only writer of the config file (SetSetting/SaveConfiguration keep
            // appSettings and disk in sync), so re-reading and re-parsing the whole XML document from disk
            // only actually needs to happen once, or when switching between the MSFS2020/2024 config
            // files. Every other caller - notably the reload that used to run after every single UI
            // setting change - was redoing this full round-trip for a file whose in-memory copy was
            // already correct and current.
            if (isLoaded && targetConfigFile == ConfigFile)
                return false;

            ConfigFile = targetConfigFile;
            xmlDoc = new();
            // A config file that is present but unreadable (seen after a PC crash during a save: the file
            // was left the right size but filled with zero bytes) must not take the whole app down with an
            // unhandled XmlException before anything is even logged. Fall back to the last known-good copy
            // (written by SaveConfiguration), else to the bundled template, and tell the user.
            if (!TryLoadXml(ConfigFile))
            {
                string backupFile = ConfigFile + ".bak";
                string templateFile = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(ConfigFile));
                string restoredFrom;
                if (TryLoadXml(backupFile)) restoredFrom = "the backup of the last good configuration";
                else if (TryLoadXml(templateFile)) restoredFrom = "the default configuration (your settings were lost)";
                else throw new FileLoadException($"Configuration file {ConfigFile} is unreadable and no backup or default is available.");
                xmlDoc.Save(ConfigFile);
                MessageBox.Show($"The configuration file\n{ConfigFile}\nwas damaged and has been restored from {restoredFrom}.",
                    "MSFS_AutoFPS Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            XmlNode xmlSettings = xmlDoc.ChildNodes[1];
            appSettings.Clear();
            foreach(XmlNode child in xmlSettings.ChildNodes)
                appSettings.Add(child.Attributes["key"].Value, child.Attributes["value"].Value);
            isLoaded = true;
            if (ConfigFile != ConfigFileLast)
            {
                ConfigFileLast = ConfigFile;
                return true;
            }
            else return false;
        }

        // Loads the file into xmlDoc and checks it has the <appSettings> element the rest of this class
        // indexes as ChildNodes[1]. Returns false (leaving xmlDoc empty) for a missing, empty or
        // unparseable file instead of throwing.
        private bool TryLoadXml(string file)
        {
            try
            {
                if (!File.Exists(file)) return false;
                string text = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(text) || text.IndexOf('\0') >= 0) return false;
                xmlDoc = new();
                xmlDoc.LoadXml(text);
                return xmlDoc.ChildNodes.Count > 1 && xmlDoc.ChildNodes[1].Name == "appSettings";
            }
            catch (XmlException)
            {
                xmlDoc = new();
                return false;
            }
        }

        public void SaveConfiguration()
        {
            foreach (XmlNode child in xmlDoc.ChildNodes[1])
                child.Attributes["value"].Value = appSettings[child.Attributes["key"].Value];

            // Write to a temporary file and swap it in, so a crash mid-write can never leave the real
            // config half-written; the previous file becomes the .bak that LoadConfiguration falls back to.
            string tempFile = ConfigFile + ".tmp";
            xmlDoc.Save(tempFile);
            if (File.Exists(ConfigFile)) File.Replace(tempFile, ConfigFile, ConfigFile + ".bak");
            else File.Move(tempFile, ConfigFile);
        }

        public bool SettingExists(string key) 
        {
            if (appSettings.ContainsKey(key)) return true;
            else return false;
        }
        public string GetSetting(string key, string defaultValue = "")
        {
            if (appSettings.ContainsKey(key))
                return appSettings[key];
            else
            {
                XmlNode newNode = xmlDoc.CreateElement("add");

                XmlAttribute attribute = xmlDoc.CreateAttribute("key");
                attribute.Value = key;
                newNode.Attributes.Append(attribute);

                attribute = xmlDoc.CreateAttribute("value");
                attribute.Value = defaultValue;
                newNode.Attributes.Append(attribute);

                xmlDoc.ChildNodes[1].AppendChild(newNode);
                appSettings.Add(key, defaultValue);
                SaveConfiguration();

                return defaultValue;
            }
        }

        public void RemoveSetting(string key)
        {
            if (appSettings.ContainsKey(key))
            {
                XmlNode nodeToRemove = xmlDoc.SelectSingleNode($"//add[@key='{key}']");
                if (nodeToRemove != null)
                {
                    nodeToRemove.ParentNode.RemoveChild(nodeToRemove);
                    appSettings.Remove(key);
                    SaveConfiguration();
                }
            }
        }
        public void SetSetting(string key, string value)
        {
            if (appSettings.TryGetValue(key, out string existing))
            {
                if (existing == value) return; // unchanged - skip rewriting the whole XML file to disk
                appSettings[key] = value;
                SaveConfiguration();
            }
        }
    }
}
