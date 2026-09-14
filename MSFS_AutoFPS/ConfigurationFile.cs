using System.Collections.Generic;
using System.IO;
using System.Security.RightsManagement;
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
            xmlDoc.LoadXml(File.ReadAllText(ConfigFile));

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

        public void SaveConfiguration()
        {
            foreach (XmlNode child in xmlDoc.ChildNodes[1])
                child.Attributes["value"].Value = appSettings[child.Attributes["key"].Value];

            xmlDoc.Save(ConfigFile);
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
