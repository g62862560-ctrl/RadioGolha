plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.radiogolha"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.radiogolha"
        minSdk = 24
        targetSdk = 35
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // shortcut: release is signed with the debug key so the APK installs as-is; use a real keystore before publishing to a store.
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    // LibVLC is ~45 MB per CPU type; one APK per ABI instead of a 190 MB universal one.
    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a", "x86_64")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }

    // Seed station list is shared with the Windows app (single source of truth).
    sourceSets["main"].assets.srcDir(layout.buildDirectory.dir("seed"))
}

val copySeed by tasks.registering(Copy::class) {
    from(rootProject.file("../src/RadioGolha/radios.json"))
    into(layout.buildDirectory.dir("seed"))
}
tasks.named("preBuild") { dependsOn(copySeed) }

dependencies {
    // LibVLC: same engine as the Windows app, needed for mms:// streams.
    implementation("org.videolan.android:libvlc-all:3.6.0")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303") // android.jar's org.json is a stub in JVM tests
}
