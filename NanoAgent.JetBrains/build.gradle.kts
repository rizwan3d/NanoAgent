import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("org.jetbrains.intellij.platform") version "2.16.0"
    kotlin("jvm") version "2.1.20"
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        create(IntelliJPlatformType.IntellijIdeaCommunity, "2024.1.7")
        pluginVerifier()
        zipSigner()
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
            create(IntelliJPlatformType.IntellijIdeaCommunity, "2024.1.7")
       }
   }
}

kotlin {
    jvmToolchain(17)
}

tasks {
    runIde {
        // JVM memory and GC settings for development
        jvmArgs("-Xmx2048m", "-XX:+UseG1GC")

        // Enable internal IDE features for plugin development
        jvmArgumentProviders += CommandLineArgumentProvider {
            listOf("-Didea.is.internal=true")
        }

        // Auto-save logs to a file for easier debugging
        systemProperty("idea.log.debug", "true")
    }

    withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
        compilerOptions {
            jvmTarget.set(JvmTarget.JVM_17)
        }
    }
}
