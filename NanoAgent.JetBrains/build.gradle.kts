import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType

plugins {
    id("org.jetbrains.intellij.platform") version "2.16.0"
    kotlin("jvm") version "2.0.21"
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        create(IntelliJPlatformType.IC, "2024.1.7")
        pluginVerifier()
        zipSigner()
        instrumentationTools()
    }
}

intellijPlatform {
    pluginConfiguration {
        id = "com.nanoagent.plugin"
        name = "NanoAgent"
        version = providers.gradleProperty("pluginVersion")
        description = """
            AI coding agent for IntelliJ-based IDEs.
            NanoAgent brings an autonomous AI coding assistant directly into your IDE,
            with file editing, shell commands, browser automation, and more.
        """.trimIndent()

        changeNotes = """
            <h3>v0.1.0</h3>
            <ul>
                <li>Initial release with ACP-based communication</li>
                <li>Chat tool window for interacting with NanoAgent</li>
                <li>Session management</li>
                <li>Streaming responses</li>
            </ul>
        """.trimIndent()

        vendor {
            name = "ALFAIN Technologies (PVT) Limited"
            url = "https://github.com/rizwan3d/NanoAgent"
        }

        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            untilBuild = providers.gradleProperty("pluginUntilBuild")
        }
    }

    pluginVerification {
        ides {
            ide(IntelliJPlatformType.IC, "2024.1.7")
        }
    }
}

kotlin {
    jvmToolchain(17)
}

tasks {
    withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
        kotlinOptions {
            jvmTarget = "17"
        }
    }
}
