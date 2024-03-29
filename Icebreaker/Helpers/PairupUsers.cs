﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Icebreaker.Helpers
{
    /// <summary>
    /// Pairuo users
    /// </summary>
    public class PairupUsers : Microsoft.Azure.Documents.Document
    {
        /// <summary>
        /// parentid
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string PairupId
        {
            get { return this.Id; }
            set { this.Id = value; }
        }

        /// <summary>
        /// Gets or sets the tenant id
        /// </summary>
        [Newtonsoft.Json.JsonProperty("ScheduledDate")]
        public System.DateTime ScheduledDate { get; set; }

        /// <summary>
        /// Gets or sets the service URL
        /// </summary>
        [Newtonsoft.Json.JsonProperty("escapedTitle")]
        public string escapedTitle { get; set; }

        /// <summary>
        /// Gets or sets the name of the person that installed the bot to the team
        /// </summary>
        [Newtonsoft.Json.JsonProperty("FirstPersonFirstName")]
        public string FirstPersonFirstName { get; set; }

        [Newtonsoft.Json.JsonProperty("SecondPersonFirstName")]
        public string SecondPersonFirstName { get; set; }

        /// <summary>
        /// Gets sets value
        /// </summary>
        [Newtonsoft.Json.JsonProperty("PersonUpn")]
        public string PersonUpn { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether gets sets value
        /// </summary>
        [Newtonsoft.Json.JsonProperty("Ispaired")]
        public bool Ispaired { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether team id
        /// </summary>
        [Newtonsoft.Json.JsonProperty("TeamId")]
        public string TeamId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether ServiceURL
        /// </summary>
        [Newtonsoft.Json.JsonProperty("ServiceURL")]
        public string ServiceURL { get; set; }

        /// <summary>
        /// override
        /// </summary>
        /// <returns>string</returns>
        public override string ToString()
        {
            return $"Pairup - Id = {this.PairupId}, ScheduledDate = {this.ScheduledDate}, escapedTitle = {this.escapedTitle}, firstPersonFirstName = {this.FirstPersonFirstName},SecondPersonFirstName = {this.SecondPersonFirstName},personUpn={this.PersonUpn},Ispaired={this.Ispaired},TeamId={this.TeamId},ServiceURL={this.ServiceURL}";
        }
    }
}