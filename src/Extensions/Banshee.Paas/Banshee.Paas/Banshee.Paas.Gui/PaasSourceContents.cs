// 
// PaasSourceContents.cs
//  
// Authors:
//   Gabriel Burt <gburt@novell.com>
//   Mike Urbanski <michael.c.urbanski@gmail.com>
//
// Copyright (C) 2009 Michael C. Urbanski
// Copyright (C) 2008 Novell, Inc.
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;

using Hyena.Data;
using Hyena.Data.Gui;

using Banshee.Sources;
using Banshee.Sources.Gui;

using Banshee.Collection;
using Banshee.ServiceStack;

using Banshee.Paas.Data;

namespace Banshee.Paas.Gui
{
    public class PaasSourceContents : FilteredListSourceContents, ITrackModelSourceContents
    {
        
        public PaasSourceContents () : base ("paas")
        {
        }

        private PaasItemView item_view;
        private PaasChannelView channel_view;
/*        
        private PodcastUnheardFilterView unheard_view;
        private DownloadStatusFilterView download_view;
*/        
        
        protected override void InitializeViews ()
        {
/*
            SetupFilterView (unheard_view = new PodcastUnheardFilterView ());
            SetupFilterView (download_view = new DownloadStatusFilterView ());
*/
            SetupMainView   (item_view    = new PaasItemView ());
            SetupFilterView (channel_view = new PaasChannelView ());
             
        }
        
        protected override void ClearFilterSelections ()
        {

            if (channel_view.Model != null) {
                channel_view.Selection.Clear ();
/*
                unheard_view.Selection.Clear ();
                download_view.Selection.Clear ();
*/
            }            
        }

        protected override bool ActiveSourceCanHasBrowser {
            get {
                DatabaseSource db_src = ServiceManager.SourceManager.ActiveSource as DatabaseSource;
                return db_src != null && db_src.ShowBrowser;
            }
        }

        #region Implement ISourceContents

        public override bool SetSource (ISource source)
        {
            DatabaseSource track_source = source as DatabaseSource;
            
            if (track_source == null) {
                return false;
            }
            
            this.source = source;
            
            SetModel (item_view, track_source.TrackModel);
            
            foreach (IListModel model in track_source.CurrentFilters) {
                if (model is PaasChannelModel) {
                    SetModel (channel_view, (model as IListModel<PaasChannel>));
                }
/*
                else if (model is PodcastUnheardFilterModel)
                    SetModel (unheard_view, (model as IListModel<OldNewFilter>));
                else if (model is DownloadStatusFilterModel)
                    SetModel (download_view, (model as IListModel<DownloadedStatusFilter>));
                else
                    Hyena.Log.DebugFormat ("PodcastContents got non-feed filter model: {0}", model);
*/                    
            }

            item_view.HeaderVisible = true;
            
            return true;
        }

        public override void ResetSource ()
        {
/*        
            SetModel (unheard_view, null);
            SetModel (download_view, null);
*/            
            source = null;
            
            SetModel (item_view, null);
            SetModel (channel_view, null);
            item_view.HeaderVisible = false;
        }

        #endregion

        #region ITrackModelSourceContents implementation 
        
        public IListView<TrackInfo> TrackView {
            get { return item_view; }
        }
        
        #endregion 
    }
}
