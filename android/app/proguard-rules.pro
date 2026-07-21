# Gson uses reflection to (de)serialize DTOs — keep field names/types intact
# for every class in the API model package, plus generic signatures/annotations
# that Gson's TypeToken machinery relies on.
-keepattributes Signature
-keepattributes *Annotation*
-keepattributes EnclosingMethod
-keepattributes InnerClasses

-keep class com.vkenterprises.crmrs.data.models.** { *; }
-keep class com.vkenterprises.crmrs.data.api.** { *; }

-keepclassmembers,allowobfuscation class * {
    @com.google.gson.annotations.SerializedName <fields>;
}
-keep class com.google.gson.reflect.TypeToken
-keep class * extends com.google.gson.reflect.TypeToken

# Retrofit uses reflection on service interface method signatures/annotations.
-keepattributes Exceptions
-keep,allowobfuscation interface com.vkenterprises.crmrs.data.api.**

# Room entities/DAOs are annotation-processed at compile time; keep entity
# fields so column mapping survives (defensive — Room ships its own rules too).
-keep class com.vkenterprises.crmrs.data.local.** { *; }

# ML Kit text recognition loads its model classes via reflection.
-keep class com.google.mlkit.vision.text.** { *; }
-keep class com.google.android.gms.internal.mlkit_vision_text.** { *; }
