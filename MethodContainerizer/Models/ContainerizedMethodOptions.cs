namespace MethodContainerizer.Models
{
    internal class ContainerizedMethodOptions
    {
        /// <summary>
        /// Number of method containers to have online at any given time
        /// </summary>
        public int MinimumAvailable { get; internal set; }
        
        /// <summary>
        /// Only create containers when the method is called
        /// </summary>
        public bool CreateAsNeeded { get; internal set; }
        
        /// <summary>
        /// Indicates whether the API is locked behind authorization
        /// </summary>
        public bool IsOpen { get; set; }
        
        /// <summary>
        /// Indicates a provided bearer should be used for authentication instead of auto-generated
        /// </summary>
        public string CustomBearer { get; set; }

        internal ContainerizedMethodOptions()
        {
            MinimumAvailable = 1;
            IsOpen = false;
        }
    }
}