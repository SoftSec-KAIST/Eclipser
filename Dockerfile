# For other Ubuntu versions, you should also fix the URL of .NET Core install
FROM ubuntu:18.04

WORKDIR /root/

### Prepare apt
RUN sed -i 's/# deb-src http/deb-src http/g' /etc/apt/sources.list
ENV DEBIAN_FRONTEND="noninteractive"

# Install basic utilities
RUN apt-get update && \
    apt-get -yy install \
      wget apt-transport-https git unzip \
      build-essential libtool libtool-bin gdb \
      automake autoconf bison flex python

# Install dependencies for QEMU used in Eclipser
RUN apt-get -yy build-dep qemu

# Install .NET Core for Eclipser
RUN wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt-get update && apt-get -yy install dotnet-sdk-2.1 && \
    rm -f packages-microsoft-prod.deb
# Disallow sending telemtry data from .net
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# Create a user and switch account
RUN useradd -ms /bin/bash test
USER test
WORKDIR /home/test

# Download and build Eclipser
RUN git clone https://github.com/SoftSec-KAIST/Eclipser.git && \
    cd Eclipser && \
    make
