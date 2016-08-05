﻿/*
 * Copyright (c) 2016, salesforce.com, inc.
 * All rights reserved.
 * Redistribution and use of this software in source and binary forms, with or
 * without modification, are permitted provided that the following conditions
 * are met:
 * - Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 * - Redistributions in binary form must reproduce the above copyright notice,
 * this list of conditions and the following disclaimer in the documentation
 * and/or other materials provided with the distribution.
 * - Neither the name of salesforce.com, inc. nor the names of its contributors
 * may be used to endorse or promote products derived from this software without
 * specific prior written permission of salesforce.com, inc.
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using Salesforce.SDK.Analytics.Model;
using Salesforce.SDK.Core;
using Salesforce.SDK.Logging;
using PCLStorage;

namespace Salesforce.SDK.Analytics.Store
{
    public class EventStoreManager : IEventStoreManager
    {
        private string _filenameSuffix;
        private readonly IFolder _rootDir;
        private string _encryptionKey;
        private bool _isLoggingEnabled = true;
        private int _maxEvents = 1000;
        private static ILoggingService LoggingService => SDKServiceLocator.Get<ILoggingService>();
        public string FilenameSuffix
        {
            get { return _filenameSuffix; }
            set { _filenameSuffix = value; }
        } 

        public string EncryptionKey
        {
            get { return _encryptionKey; }
            set { _encryptionKey = value; }
        }

        public EventStoreManager(string fileNameSuffix, string encryptionKey)
        {
            FilenameSuffix = fileNameSuffix;
            EncryptionKey = encryptionKey;
            _rootDir = FileSystem.Current.LocalStorage;
        }

        public async Task StoreEventAsync(InstrumentationEvent instrumentationEvent)
        {
            if (instrumentationEvent == null || string.IsNullOrEmpty(instrumentationEvent.ToJson().ToString()))
            {
                LoggingService.Log("Invalid Event", LoggingLevel.Error);
                return;
            }
            //TODO: Add check for shouldstoreevent
            var fileName = instrumentationEvent.EventId + _filenameSuffix;
            //Open file
            IFile file = await _rootDir.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            //Wrtie to file after encrypting contents
            //TODO: Enrypt the contents
            await file.WriteAllTextAsync(instrumentationEvent.ToJson().ToString());
        }

        public async Task StoreEventsAsync(List<InstrumentationEvent> instrumentationEvents)
        {
            if (instrumentationEvents == null || instrumentationEvents.Count == 0)
            {
                LoggingService.Log("No events to store", LoggingLevel.Error);
                return;
            }
            if (!await ShouldStoreEvent())
            {
                return;
            }
            foreach (var instrumentationEvent in instrumentationEvents)
            {
                await StoreEventAsync(instrumentationEvent);
            }
        }

        public async Task<InstrumentationEvent> FetchEventAsync(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                LoggingService.Log("Invalid event ID supplied: " + eventId, LoggingLevel.Error);
                return null;
            }
            var fileName = eventId + _filenameSuffix;
            var file = await _rootDir.GetFileAsync(fileName);
            return await FetchEventAsync(file);
        }

        public async Task<List<InstrumentationEvent>> FetchAllEventsAsync()
        {
            var files = await _rootDir.GetFilesAsync();
            var events = new List<InstrumentationEvent>();

            foreach (var file in files)
            {
                var instrumentationEvent = await FetchEventAsync(file.Name);
                if (instrumentationEvent != null)
                {
                    events.Add(instrumentationEvent);
                }
            }

            return events;
        }

        public async Task<bool> DeleteEventAsync(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                LoggingService.Log("Invalid event ID supplied: " + eventId, LoggingLevel.Error);
                return false;
            }
            var fileName = eventId + _filenameSuffix;
            var file = await _rootDir.GetFileAsync(fileName);
            try
            {
                await file.DeleteAsync();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task DeleteEventsAsync(List<string> eventIds)
        {
            if (eventIds == null || eventIds.Count == 0)
            {
                LoggingService.Log("No events to delete", LoggingLevel.Error);
                return;
            }
            foreach (var eventId in eventIds)
            {
                await DeleteEventAsync(eventId);
            }
        }

        public async Task DeleteAllEventsAsync()
        {
            var files = await _rootDir.GetFilesAsync();
            foreach (var file in files)
            {
                await file.DeleteAsync();
            }
        }

        public async Task ChangeEncryptionKeyAsync(string oldKey, string newKey)
        {
            var storedEvents = await FetchAllEventsAsync();
            await DeleteAllEventsAsync();
            _encryptionKey = newKey;
            await StoreEventsAsync(storedEvents);
        }

        public void DisableEnableLogging(bool enabled)
        {
            _isLoggingEnabled = enabled;
        }

        public bool IsLoggingEnabled()
        {
            return _isLoggingEnabled;
        }

        public void SetMaxEvents(int maxEvents)
        {
            _maxEvents = maxEvents;
        }

        private async Task<InstrumentationEvent> FetchEventAsync(IFile file)
        {
            if (file == null)
            {
                LoggingService.Log("File does not exist", LoggingLevel.Error);
                return null;
            }
            InstrumentationEvent  instrumentationEvent = null;
            var json = await file.ReadAllTextAsync();
            //TODO: decrypt contents read from file
            //TODO: Add null checks, throw exceptions
            instrumentationEvent = new InstrumentationEvent(new JObject(json));

            return instrumentationEvent;
        }

        private async Task<bool> ShouldStoreEvent()
        {
            var files = await _rootDir.GetFilesAsync();
            int filesCount = 0;
            if (files != null)
            {
                filesCount = files.Count;
            }
            return _isLoggingEnabled && (filesCount < _maxEvents);
        }

        //TODO: encrypt method decrypt method
    }
}
