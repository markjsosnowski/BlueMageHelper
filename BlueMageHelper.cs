using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using System.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Runtime.InteropServices;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace BlueMageHelper
{
    public sealed class BlueMageHelper : IDalamudPlugin
    {
        public string Name => "Blue Mage Helper";
        //private const string commandName = "/blu";

        private DalamudPluginInterface PluginInterface { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUI { get; init; }
        private Framework Framework { get; init; }
        private GameGui GameGui { get; init; }

        private JObject spell_sources;
        private const int blank_text_textnode_index = 54;
        private const int spell_number_textnode_index = 62;
        private const int spell_name_textnode_index = 61;
        private const string sources_list_url = "https://markjsosnowski.github.io/FFXIV/spell_sources.json";
        private const int expected_nodelistcount = 4;

        public BlueMageHelper(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] Framework framework,
            [RequiredVersion("1.0")] GameGui gameGui)
        {
            this.PluginInterface = pluginInterface;
            this.Framework = framework;
            this.GameGui = gameGui;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);
            this.PluginUI = new PluginUI(this.Configuration);

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            Framework.Update += AOZNotebook_addon_manager;

            try
            {
                using (WebClient wc = new WebClient())
                {
                    var spell_sources_json_string = wc.DownloadString(sources_list_url);
                    this.spell_sources = JObject.Parse(spell_sources_json_string);
                }
            }
            catch (WebException e)
            {
                PluginLog.Error("There was a problem accessing the spell list. Is GitHub down?", e);
                this.spell_sources = null;
            }
        }

        private void AOZNotebook_addon_manager(Framework framework)
        {
            try
            {
                var addon_ptr = GameGui.GetAddonByName("AOZNotebook", 1);
                if (addon_ptr == IntPtr.Zero)
                    return;
                spellbook_writer(addon_ptr);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                PluginLog.Verbose("Blue Mage Helper caught an exception: "+ e);
            }
        }

        private unsafe void spellbook_writer(IntPtr addon_ptr)
        {
            string spell_number_string = string.Empty;
            string hint_text;
            //AddonAOZNotebook* spellbook_addon = (AddonAOZNotebook*)addon_ptr;
            AtkUnitBase* spellbook_base_node = (AtkUnitBase*)addon_ptr;
            if (spellbook_base_node->UldManager.NodeListCount < spell_number_textnode_index + 1)
                return;
            AtkTextNode* spell_name_textnode = (AtkTextNode*)spellbook_base_node->UldManager.NodeList[spell_name_textnode_index];
            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string spell_name = Marshal.PtrToStringAnsi(new IntPtr(spell_name_textnode->NodeText.StringPtr));
            #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            if (spell_name != "???" && !this.Configuration.show_hint_even_if_unlocked) // Don't need to show hints for already known spells. Maybe make this a config value.
                return;
            AtkTextNode* empty_textnode = (AtkTextNode*)spellbook_base_node->UldManager.NodeList[blank_text_textnode_index];
            AtkTextNode* spell_number_textnode = (AtkTextNode*)spellbook_base_node->UldManager.NodeList[spell_number_textnode_index];
            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            spell_number_string = Marshal.PtrToStringAnsi(new IntPtr(spell_number_textnode->NodeText.StringPtr));
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            spell_number_string = extract_spell_number(spell_number_string);
            //spell_number_string = spell_number_string.Substring(1); // Remove the # from the spell number
            #pragma warning restore CS8602 // Dereference of a possibly null reference.
            hint_text = get_hint_text(spell_number_string);
            empty_textnode->ResizeNodeForCurrentText();
            //TODO if there is already text in the box, append a new line instead
            empty_textnode->SetText(hint_text);

            empty_textnode->AtkResNode.ToggleVisibility(true);
        }

        /* Works if the blue mmage spellbook is already open. crashes if trying to open the spellbook from a closed state.
         * 2021-12-01 17:10:34.247 -05:00 [DBG] [BlueMageHelper] Extracted  from 
         * Not sure how it's getting an empty string if i'm checking for that to exist
         * Also not sure where it's crashing even if it's an empty string??
         */
        private string extract_spell_number(string spell_number_string)
        {
            try
            {
                Regex spell_number_regex = new Regex(@"\d+");
                Match regex_match = spell_number_regex.Match(spell_number_string);
                string spell_number = regex_match.Value;
                PluginLog.Debug("Extracted " + regex_match.Value + " from " + spell_number_string);
                return spell_number;
            }
            catch(Exception e)
            {
                PluginLog.Debug("Exception was caught in the extract_spell_number function" + e);
                return "1";
            }

        }

        //TODO use monster IDs and perform a sheets lookup
        private string get_hint_text(string spell_number)
        {
            if (this.spell_sources.ContainsKey(spell_number))
                #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                #pragma warning disable CS8603 // Possible null reference return.
                return (string)spell_sources[spell_number];
            else
                return "No data for spell #" + spell_number + "";
        }

        public void Dispose()
        {
            this.PluginUI.Dispose();
            //this.CommandManager.RemoveHandler(commandName);
            Framework.Update -= AOZNotebook_addon_manager;
        }

        private void OnCommand(string command, string args)
        {
            // in response to the slash command, just display our main ui
            this.PluginUI.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUI.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUI.SettingsVisible = true;
        }
    }
}
