namespace Snow.Levels
{
    /// <summary>
    /// The weather a location's sounds follow (0..1 each), written every frame by the location's ambience (Forest:
    /// ForestAmbience, from the wind it already hears). Read by LocationSounds' emitter conditions (day, night, windy,
    /// storm). With no ambience: a calm day.
    /// </summary>
    public static class LocationWeather
    {
        public static float Day = 1f, Gust, Storm;
    }
}
