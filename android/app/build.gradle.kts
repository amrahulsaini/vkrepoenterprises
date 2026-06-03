import groovy.json.JsonSlurper

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("com.google.dagger.hilt.android")
    kotlin("kapt")
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-tenant product flavors, loaded from android/tenants.json so we never
// hand-edit Gradle when a new agency is approved. Each entry produces a
// distinct, signable APK / AAB with its own applicationId, launcher name,
// primary color and bundled AGENCY_SLUG.
// ─────────────────────────────────────────────────────────────────────────────
data class Tenant(
    val slug: String, val name: String,
    val mobile: String, val address: String,
    val primaryColor: String
) {
    /** Source-set dir & flavor name — underscores stripped (Gradle plays nicer). */
    val flavor: String get() = slug.replace("_", "")
    /** Last segment of applicationId — must be a valid Java package atom. */
    val pkg: String    get() = slug.replace("_", "")
}

val tenants: List<Tenant> = run {
    val f = rootProject.file("tenants.json")
    if (!f.exists()) throw GradleException("tenants.json missing at ${f.absolutePath}")
    @Suppress("UNCHECKED_CAST")
    val raw = JsonSlurper().parse(f) as List<Map<String, String>>
    raw.map {
        Tenant(
            slug         = it["slug"]!!,
            name         = it["name"]!!,
            mobile       = it["mobile"]   ?: "",
            address      = it["address"]  ?: "",
            primaryColor = it["primaryColor"] ?: "#FF6B35"
        )
    }
}

android {
    namespace   = "com.vkenterprises.vras"
    compileSdk  = 35

    defaultConfig {
        // Per-flavor applicationId overrides this — kept only as a fallback.
        applicationId = "com.crmrecoverysoftware.crms"
        minSdk        = 26
        targetSdk     = 35
        versionCode   = 33
        versionName   = "1.0.32"

        buildConfigField("String", "BASE_URL", "\"https://api.crmrecoverysoftware.com/\"")
    }

    // ── Release signing ─────────────────────────────────────────────────────
    // Single keystore signs every per-agency variant. The keystore lives at
    // android/keystore/release.keystore — gitignored, never committed. If
    // CRMS_KEYSTORE_PASSWORD / CRMS_KEY_PASSWORD env vars are set they take
    // precedence over the in-tree default (for CI / shared dev machines).
    val releaseKeystore = rootProject.file("keystore/release.keystore")
    if (releaseKeystore.exists()) {
        signingConfigs {
            create("release") {
                storeFile     = releaseKeystore
                storePassword = System.getenv("CRMS_KEYSTORE_PASSWORD") ?: "crms@kc.12"
                keyAlias      = "crms"
                keyPassword   = System.getenv("CRMS_KEY_PASSWORD")     ?: "crms@kc.12"
            }
        }
    }

    flavorDimensions += "agency"
    productFlavors {
        tenants.forEach { t ->
            create(t.flavor) {
                dimension      = "agency"
                applicationId  = "com.crmrecoverysoftware.${t.pkg}"
                versionCode    = 33
                versionName    = "1.0.32"
                // Bundled into BuildConfig so the app pre-binds to this tenant
                // — no agency picker on the login screen.
                buildConfigField("String", "AGENCY_SLUG",    "\"${t.slug}\"")
                buildConfigField("String", "AGENCY_NAME",    "\"${t.name}\"")
                buildConfigField("String", "AGENCY_MOBILE",  "\"${t.mobile}\"")
                buildConfigField("String", "AGENCY_ADDRESS", "\"${t.address}\"")
                // Picked up by the manifest as android:label="@string/app_name".
                resValue("string", "app_name",        t.name)
                resValue("color",  "tenant_primary", t.primaryColor)
            }
        }
    }

    buildTypes {
        release {
            // Keep minify off for now — Compose + Hilt + Retrofit + Room +
            // WorkManager already need a bunch of keep rules to survive R8
            // and turning it on without thorough testing tends to break
            // reflection-heavy bits. Re-enable after the first Play Store
            // round-trip is stable.
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            if (releaseKeystore.exists()) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }

    buildFeatures {
        compose      = true
        buildConfig  = true
    }
    composeOptions { kotlinCompilerExtensionVersion = "1.5.14" }

    // Release builds otherwise run lintVital, which isn't needed to produce the
    // signed APK and has intermittently locked its cache on Windows. Skip it.
    lint {
        checkReleaseBuilds = false
        abortOnError       = false
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.06.00")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")

    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.3")
    implementation("androidx.activity:activity-compose:1.9.0")
    implementation("androidx.navigation:navigation-compose:2.7.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.3")

    // Hilt DI
    implementation("com.google.dagger:hilt-android:2.51.1")
    kapt("com.google.dagger:hilt-android-compiler:2.51.1")
    implementation("androidx.hilt:hilt-navigation-compose:1.2.0")

    // Network
    implementation("com.squareup.retrofit2:retrofit:2.11.0")
    implementation("com.squareup.retrofit2:converter-gson:2.11.0")
    implementation("com.squareup.okhttp3:logging-interceptor:4.12.0")

    // Image
    implementation("io.coil-kt:coil-compose:2.6.0")

    // Storage
    implementation("androidx.datastore:datastore-preferences:1.1.1")

    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")

    // Room
    val roomVersion = "2.6.1"
    implementation("androidx.room:room-runtime:$roomVersion")
    implementation("androidx.room:room-ktx:$roomVersion")
    kapt("androidx.room:room-compiler:$roomVersion")

    // WorkManager + Hilt integration
    implementation("androidx.work:work-runtime-ktx:2.9.1")
    implementation("androidx.hilt:hilt-work:1.2.0")
    kapt("androidx.hilt:hilt-compiler:1.2.0")

    // Location
    implementation("com.google.android.gms:play-services-location:21.3.0")

    // On-device OCR — reads the 12-digit Aadhaar number off the front photo at
    // registration so the agent doesn't type it (Latin text recognition, models
    // bundled in the APK, fully offline).
    implementation("com.google.mlkit:text-recognition:16.0.1")
}

kapt { correctErrorTypes = true }
