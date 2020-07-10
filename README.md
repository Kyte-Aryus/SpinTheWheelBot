# SpinTheWheelBot
SpinTheWheelBot is a bot written for Discord which is designed to award admin-defined prizes based on random chance. Currently the prizes consist of roles that can be configured to be removed after a time delay or kept indefinitely.

There are several features 
- Define prizes which award Discord guild roles at specific odds
- Configure a consolation prize to give guild memebers a particular role when no other prize is won
- Optionally enforce a penalty for spinning too often (this reduces the benefit for members to write scripts to 'game' the bot)
- Special configuration parameters exist for roles designed to temporarily restrict access or mute spinners (spin at oyur own risk!)
- The Big Red Button! Activate the button to allow any user to smash it. Awards an admin-defined role if pressed
- Each of the above features can be enabled or disabled in the configuration

#### Why use this bot?
This bot is designed just for fun. While only role-based prizes are supported right now, you can custom define your own roles to do anything you want to with the prizes. 

Some examples could be:
- Award a rare role at very low chance which displays above the other roles
- Offset the chance of winning a rare benefical role by having a chance of a silence which temporarily removes send message permissions and voice chat connect permissions
- Have the consolation prize be a quick silence
- Award special privileges temporarily to a specific channel

## Compiling and Deploying
SpinTheWheelBot is designed to be built with Visual Studio 2017 and deployed as a .NET Core application for cross-platform compatability. It can be configured as a service on Linux.

TBD

## Configuration Fields
TBD

## Running
TBD
