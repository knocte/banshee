//
// DapSource.cs
//
// Author:
//   Gabriel Burt <gburt@novell.com>
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2008 Novell, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Threading;
using Mono.Unix;

using Hyena;
using Hyena.Data.Sqlite;

using Banshee.Base;
using Banshee.ServiceStack;
using Banshee.Sources;
using Banshee.Collection;
using Banshee.Collection.Database;
using Banshee.Hardware;
using Banshee.MediaEngine;
using Banshee.MediaProfiles;

using Banshee.Dap.Gui;

namespace Banshee.Dap
{
    public abstract class DapSource : RemovableSource, IDisposable
    {
        private DapInfoBar dap_info_bar;
        // private DapPropertiesDisplay dap_properties_display;
        
        private IDevice device;
        internal IDevice Device {
            get { return device; }
        }
        
        private string addin_id;
        internal string AddinId {
            get { return addin_id; }
            set { addin_id = value; }
        }
        
        private MediaGroupSource music_group_source;
        protected MediaGroupSource MusicGroupSource {
            get { return music_group_source; }
        }
        
        private MediaGroupSource video_group_source;
        protected MediaGroupSource VideoGroupSource {
            get { return video_group_source; }
        }
        
        protected DapSource ()
        {
        }

        public virtual void DeviceInitialize (IDevice device)
        {
            this.device = device;
            type_unique_id = device.Uuid;
        }

        public override void Dispose ()
        {
            PurgeBuiltinSmartPlaylists ();
            PurgeTracks ();
            
            if (dap_info_bar != null) {
                dap_info_bar.Destroy ();
                dap_info_bar = null;
            }
            
            Properties.Remove ("Nereid.SourceContents.FooterWidget");
            
            /*Properties.Remove ("Nereid.SourceContents");
            dap_properties_display.Destroy ();
            dap_properties_display = null;*/
        }
        
        private void PurgeBuiltinSmartPlaylists ()
        {
            ServiceManager.DbConnection.Execute (new HyenaSqliteCommand (@"
                BEGIN TRANSACTION;
                    DELETE FROM CoreSmartPlaylistEntries WHERE SmartPlaylistID IN
                        (SELECT SmartPlaylistID FROM CoreSmartPlaylists WHERE PrimarySourceID = ?);
                    DELETE FROM CoreSmartPlaylists WHERE PrimarySourceID = ?;   
                COMMIT TRANSACTION",
                DbId, DbId
            ));
        }
        
        internal void RaiseUpdated ()
        {
            OnUpdated ();
        }

#region Source

        protected override void Initialize ()
        {
            PurgeBuiltinSmartPlaylists ();
            
            base.Initialize ();
            
            Expanded = true;
            Properties.SetStringList ("Icon.Name", GetIconNames ());
            Properties.Set<string> ("SourcePropertiesActionLabel", Catalog.GetString ("Device Properties"));
            Properties.Set<OpenPropertiesDelegate> ("SourceProperties.GuiHandler", delegate {
                new DapPropertiesDialog (this).RunDialog ();
            });
            
            Properties.Set<bool> ("Nereid.SourceContents.HeaderVisible", false);
            
            dap_info_bar = new DapInfoBar (this);
            Properties.Set<Gtk.Widget> ("Nereid.SourceContents.FooterWidget", dap_info_bar);
            
            /*dap_properties_display = new DapPropertiesDisplay (this);
            Properties.Set<Banshee.Sources.Gui.ISourceContents> ("Nereid.SourceContents", dap_properties_display);*/

            if (String.IsNullOrEmpty (GenericName)) {
                GenericName = Catalog.GetString ("Media Player");
            }
            
            if (String.IsNullOrEmpty (Name)) {
                Name = device.Name;
            }

            AddDapProperty (Catalog.GetString ("Product"), device.Product);
            AddDapProperty (Catalog.GetString ("Vendor"), device.Vendor);
            
            if (acceptable_mimetypes == null) {
                acceptable_mimetypes = MediaCapabilities != null 
                    ? MediaCapabilities.PlaybackMimeTypes 
                    : new string [] { "taglib/mp3" };
            }
            
            music_group_source = new MusicGroupSource (this);
            video_group_source = new VideoGroupSource (this);

            AddChildSource (music_group_source);
            AddChildSource (video_group_source);
        }
        
        public override void AddChildSource (Source child)
        {
            if (child is Banshee.Playlist.AbstractPlaylistSource && !(child is MediaGroupSource)) {
                Log.Information ("Note: playlists added to digital audio players within Banshee are not yet saved to the device.", true);
            }
            
            base.AddChildSource (child);
        }
        
        public override bool HasProperties {
            get { return true; }
        }
        
        public override bool CanActivate {
            get { return false; }
        }

        public override void SetStatus (string message, bool can_close, bool is_spinning, string icon_name)
        {
            base.SetStatus (message, can_close, is_spinning, icon_name);
            foreach (Source child in Children) {
                child.SetStatus (message, can_close, is_spinning, icon_name);
            }
        }

        public override void HideStatus ()
        {
            base.HideStatus ();
            foreach (Source child in Children) {
                child.HideStatus ();
            }
        }

#endregion
        
#region Track Management/Syncing   

        public void SyncWith (PrimarySource source)
        {
            try {
                SourceSync from_music = new SourceSync (ServiceManager.SourceManager.MusicLibrary, this);
                Log.Information (from_music.ToString ());
                
                SourceSync to_music = new SourceSync (this, ServiceManager.SourceManager.MusicLibrary);
                Log.Information (to_music.ToString ());
            } catch (Exception e) {
                Log.Exception (e);
            }
        }
        
        public class SourceSync
        {
            const string intersection = @"PrimarySourceId = ? AND MetadataHash NOT IN 
                    (SELECT MetadataHash FROM CoreTracks WHERE PrimarySourceID = ?)";
            
            PrimarySource from, to;
            int count;
            long file_size;
            TimeSpan duration;
            
            public SourceSync (PrimarySource from, PrimarySource to)
            {
                this.from = from;
                this.to = to;
                Update ();
            }
            
            public void Update ()
            {
                using (new Hyena.Timer ("seeing what there is to sync")) {
                    using (HyenaDataReader reader = new HyenaDataReader (ServiceManager.DbConnection.Query (SelectSql (
                        "COUNT(*), SUM(FileSize), SUM(Duration)")))) {
                        count = reader.Get<int> (0);
                        file_size = reader.Get<long> (1);
                        duration = reader.Get<TimeSpan> (2); 
                    }
                }
            }
                    
            private HyenaSqliteCommand SelectSql (string select)
            {
                return new HyenaSqliteCommand (
                    String.Format ("SELECT {0} FROM CoreTracks WHERE {1}", select, intersection),
                    from.DbId, to.DbId
                );
            }
            
            public int Count {
                get { return count; }
            }
            
            public long FileSize {
                get { return file_size; }
            }
            
            public TimeSpan Duration {
                get { return duration; }
            }
            
            public override string ToString ()
            {
                return String.Format ("There are {0} items, {1} MB, and {2} to sync from {3} to {4}",
                    count, file_size/(1024*1024), duration, from, to);
            }
        }
        
        public void LoadDeviceContents ()
        {
            ThreadPool.QueueUserWorkItem (ThreadedLoadDeviceContents);
        }
        
        private void ThreadedLoadDeviceContents (object state)
        {
            try {
                PurgeTracks ();
                SetStatus (String.Format (Catalog.GetString ("Loading {0}"), Name), false);
                LoadFromDevice ();
                OnTracksAdded ();
                HideStatus ();
                
                SyncWith (ServiceManager.SourceManager.MusicLibrary);
            } catch (Exception e) {
                Log.Exception (e);
            }
        }

        protected virtual void LoadFromDevice ()
        {
        }
        
        protected abstract void AddTrackToDevice (DatabaseTrackInfo track, SafeUri fromUri);  

        protected bool TrackNeedsTranscoding (TrackInfo track)
        {
            foreach (string mimetype in AcceptableMimeTypes) {
                if (ServiceManager.MediaProfileManager.GetExtensionForMimeType (track.MimeType) == 
                    ServiceManager.MediaProfileManager.GetExtensionForMimeType (mimetype)) {
                    return false;
                }
            }

            return true;
        }

        public struct DapProperty {
            public string Name;
            public string Value;
            public DapProperty (string k, string v) { Name = k; Value = v; }
        }

        private List<DapProperty> dap_properties = new List<DapProperty> ();
        protected void AddDapProperty (string key, string val)
        {
            dap_properties.Add (new DapProperty (key, val));
        }

        public IEnumerable<DapProperty> DapProperties {
            get { return dap_properties; }
        }

        protected override void AddTrackAndIncrementCount (DatabaseTrackInfo track)
        {
            if (!TrackNeedsTranscoding (track)) {
                AddTrackToDevice (track, track.Uri);
                IncrementAddedTracks ();
                return;
            }

            // If it's a video and needs transcoding, we don't support that yet
            // TODO have preferred profiles for Audio and Video separately
            if (PreferredConfiguration == null || (track.MediaAttributes & TrackMediaAttributes.VideoStream) != 0) {
                string format = System.IO.Path.GetExtension (track.Uri.LocalPath);
                format = String.IsNullOrEmpty (format) ? Catalog.GetString ("Unknown") : format.Substring (1);
                throw new ApplicationException (String.Format (Catalog.GetString (
                    "The {0} format is not supported by the device, and no converter was found to convert it."), format));
            }

            TranscoderService transcoder = ServiceManager.Get<TranscoderService> ();
            if (transcoder == null) {
                throw new ApplicationException (Catalog.GetString (
                    "File format conversion is not supported for this device."));
            }
            
            transcoder.Enqueue (track, PreferredConfiguration, OnTrackTranscoded, OnTrackTranscodeCancelled);
        }
        
        private void OnTrackTranscoded (TrackInfo track, SafeUri outputUri)
        {
            AddTrackJob.Status = String.Format ("{0} - {1}", track.ArtistName, track.TrackTitle);
            
            try {
                AddTrackToDevice ((DatabaseTrackInfo)track, outputUri);
            } catch (Exception e) {
                Log.Exception (e);
            }
            
            IncrementAddedTracks ();
        }
        
        private void OnTrackTranscodeCancelled ()
        {
            IncrementAddedTracks (); 
        }
        
#endregion

#region Device Properties

        protected virtual string [] GetIconNames ()
        {
            string vendor = device.Vendor;
            string product = device.Product;
            
            vendor = vendor != null ? vendor.Trim () : null;
            product = product != null ? product.Trim () : null;

            if (!String.IsNullOrEmpty (vendor) && !String.IsNullOrEmpty (product)) {
                return new string [] { 
                    String.Format ("multimedia-player-{0}-{1}", vendor, product).Replace (' ', '-').ToLower (), 
                    FallbackIcon
                };
            } else {
                return new string [] { FallbackIcon };
            }
        }
        
        private string FallbackIcon {
            get { return "multimedia-player"; }
        }

        protected virtual bool HasMediaCapabilities {
            get { return MediaCapabilities != null; }
        }

        protected virtual IDeviceMediaCapabilities MediaCapabilities {
            get { return device.MediaCapabilities; }
        }
        
        private ProfileConfiguration preferred_config;
        private ProfileConfiguration PreferredConfiguration {
            get {
                if (preferred_config != null) {
                    return preferred_config;
                }
            
                MediaProfileManager manager = ServiceManager.MediaProfileManager;
                if (manager == null) {
                    return null;
                }
        
                preferred_config = manager.GetActiveProfileConfiguration (UniqueId, acceptable_mimetypes);
                return preferred_config;
            }
        }

        private string [] acceptable_mimetypes;
        public string [] AcceptableMimeTypes {
            get { return acceptable_mimetypes; }
            protected set { acceptable_mimetypes = value; }
        }
       
        public long BytesVideo {
            get { return VideoGroupSource == null ? 0 : VideoGroupSource.BytesUsed; }
        }
        
        public long BytesMusic {
            get { return MusicGroupSource == null ? 0 : MusicGroupSource.BytesUsed; }
        }
        
#endregion
        
    }
}
